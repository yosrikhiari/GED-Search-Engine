using GED.Core.Interfaces;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System.Text;
using System.Text.Json;

namespace GED.Infrastructure.Services;

/// <summary>
/// Resilient RabbitMQ service with lazy connection and automatic reconnection.
/// 
/// <para>
/// Key features:
/// <list type="bullet">
///   <item>
///     <term>Lazy connection</term>
///     <description>
///       Connection is established on first use, not at construction.
///     </description>
///   </item>
///   <item>
///     <term>Automatic reconnection</term>
///     <description>
///       Handles connection failures with exponential backoff retries.
///     </description>
///   </item>
///   <item>
///     <term>Thread-safe</term>
///     <description>
///       Uses semaphore for connection access and disposal.
///     </description>
///   </item>
///   <item>
///     <term>RabbitMQ.Client v7 compatible</term>
///     <description>
///       Uses async API methods (CreateConnectionAsync, CreateChannelAsync).
///     </description>
///   </item>
/// </list>
/// </para>
/// 
/// <para>
/// Message persistence: All published messages use Persistent = true to survive
/// broker restarts. Un persistent messages are lost on shutdown.
/// </para>
/// </summary>
public class RabbitMqService : IMessageQueueService, IAsyncDisposable, IDisposable
{
    private readonly ILogger<RabbitMqService> _logger;

    /// <summary>
    /// Connection factory with connection parameters.
    /// </summary>
    private readonly ConnectionFactory _factory;

    /// <summary>
    /// Current connection (null until first use).
    /// </summary>
    private IConnection? _connection;

    /// <summary>
    /// Lock for thread-safe connection access.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Whether the service has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Maximum retry attempts for connection.
    /// </summary>
    private const int MaxConnectRetries = 5;

    /// <summary>
    /// Base retry delay in milliseconds (multiplied by attempt number).
    /// </summary>
    private const int BaseRetryMs = 1000;

    /// <summary>
    /// Initializes a new instance of <see cref="RabbitMqService"/>.
    /// </summary>
    /// <param name="logger">Logger for service events.</param>
    /// <param name="hostname">RabbitMQ hostname.</param>
    /// <param name="username">RabbitMQ username.</param>
    /// <param name="password">RabbitMQ password.</param>
    /// <param name="port">RabbitMQ port (defaults to 5672).</param>
    public RabbitMqService(
        ILogger<RabbitMqService> logger,
        string hostname,
        string username,
        string password,
        int port = 5672)
    {
        _logger = logger;

        _factory = new ConnectionFactory
        {
            HostName                   = hostname,
            Port                       = port,
            AutomaticRecoveryEnabled   = true,
            UserName                   = username,
            Password                   = password,
            NetworkRecoveryInterval    = TimeSpan.FromSeconds(5),
            RequestedHeartbeat         = TimeSpan.FromSeconds(60),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
            Ssl                       = { Enabled = false },
        };

        _logger.LogInformation(
            "RabbitMqService created (lazy) — will connect to {Host}:{Port} on first use",
            hostname, port);
    }

    /// <inheritdoc />
    public async Task PublishAsync<T>(
        string queueName,
        T message,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        for (int attempt = 1; attempt <= MaxConnectRetries; attempt++)
        {
            try
            {
                var conn = await GetConnectionAsync(cancellationToken);
                var ch   = await conn.CreateChannelAsync(cancellationToken: cancellationToken);
                await using (ch)
                {
                    var props = new BasicProperties
                    {
                        Persistent  = true,  // Survive broker restarts
                        ContentType = "application/json"
                    };

                    await ch.BasicPublishAsync(
                        exchange:         string.Empty,
                        routingKey:       queueName,
                        mandatory:        false,
                        basicProperties:  props,
                        body:             body,
                        cancellationToken: cancellationToken);

                    _logger.LogInformation(
                        "✅ Published message to queue '{Queue}' ({Bytes} bytes)",
                        queueName, body.Length);
                }

                return; // success
            }
            catch (Exception ex) when (
                ex is BrokerUnreachableException or
                      AlreadyClosedException or
                      OperationInterruptedException or
                      IOException)
            {
                // Connection issue — reset and retry
                await ResetConnectionAsync();

                if (attempt == MaxConnectRetries)
                {
                    _logger.LogError(ex,
                        "❌ Failed to publish to '{Queue}' after {Max} attempts",
                        queueName, MaxConnectRetries);
                    throw;
                }

                var delay = BaseRetryMs * attempt;
                _logger.LogWarning(
                    "⚠️ Publish attempt {Attempt}/{Max} failed for '{Queue}': {Error}. Retrying in {Delay}ms…",
                    attempt, MaxConnectRetries, queueName, ex.Message, delay);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Note: Subscription is handled by OcrWorkerService, not this service.
    /// This method throws NotSupportedException to enforce proper usage.
    /// </remarks>
    public Task SubscribeAsync<T>(
        string queueName,
        Func<T, Task> handler,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "SubscribeAsync is not supported by RabbitMqService. Use OcrWorkerService for message consumption.");
    }

    // ── Connection management ─────────────────────────────────────────────────

    /// <summary>
    /// Gets or creates a connection to RabbitMQ.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Active RabbitMQ connection.</returns>
    /// <remarks>
    /// Uses lazy initialization — connection is created on first publish.
    /// Thread-safe via semaphore.
    /// </remarks>
    private async Task<IConnection> GetConnectionAsync(CancellationToken ct)
    {
        // Fast path: return existing connection
        if (_connection is { IsOpen: true })
            return _connection;

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_connection is { IsOpen: true })
                return _connection;

            // Clean up stale connection
            if (_connection != null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            _logger.LogInformation("🔌 Connecting to RabbitMQ at {Host}…", _factory.HostName);

            // Connect with retries
            for (int attempt = 1; attempt <= MaxConnectRetries; attempt++)
            {
                try
                {
                    _connection = await _factory.CreateConnectionAsync(new[] { new AmqpTcpEndpoint(_factory.HostName, _factory.Port) }, ct);
                    _logger.LogInformation("✅ RabbitMQ connection established");
                    return _connection;
                }
                catch (BrokerUnreachableException ex)
                {
                    if (attempt == MaxConnectRetries)
                    {
                        _logger.LogError(ex,
                            "❌ Cannot connect to RabbitMQ after {Max} attempts", MaxConnectRetries);
                        throw;
                    }

                    var delay = BaseRetryMs * attempt;
                    _logger.LogWarning(
                        "⚠️ RabbitMQ connect attempt {Attempt}/{Max} failed. Retrying in {Delay}ms…",
                        attempt, MaxConnectRetries, delay);

                    await Task.Delay(delay, ct);
                }
            }

            throw new InvalidOperationException("RabbitMQ connection failed");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Resets the connection by disposing it.
    /// Next operation will create a new connection.
    /// </summary>
    private async Task ResetConnectionAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_connection != null)
            {
                try { await _connection.DisposeAsync(); } catch { /* best effort */ }
                _connection = null;
            }
            _logger.LogInformation("🔄 RabbitMQ connection reset — will reconnect on next use");
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── IAsyncDisposable / IDisposable ────────────────────────────────────────

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_connection != null)
        {
            try { await _connection.CloseAsync(); } catch { /* best effort */ }
            try { await _connection.DisposeAsync(); } catch { /* best effort */ }
        }
        _lock.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _connection?.CloseAsync().GetAwaiter().GetResult(); } catch { /* best effort */ }
        try { _connection?.DisposeAsync().GetAwaiter().GetResult(); } catch { /* best effort */ }
        try { _lock.Dispose(); } catch { /* best effort */ }
    }
}

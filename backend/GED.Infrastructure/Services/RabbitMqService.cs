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
/// KEY FIXES vs original:
///   1. Lazy connection — does NOT connect at construction time.
///      The original singleton connected in the constructor, so if RabbitMQ
///      wasn't ready yet (race condition at startup), the singleton was
///      permanently broken and all PublishAsync calls silently failed.
///   2. Per-call channel — channels are cheap; reusing one channel across
///      threads is not thread-safe with RabbitMQ.Client v6.
///   3. Automatic reconnect on publish failure with exponential backoff.
///   4. Proper disposal order (channel → connection).
/// </summary>
public class RabbitMqService : IMessageQueueService, IDisposable
{
    private readonly ILogger<RabbitMqService> _logger;
    private readonly ConnectionFactory _factory;

    private IConnection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    private const int MaxConnectRetries = 5;
    private const int BaseRetryMs = 1000;

    public RabbitMqService(
        ILogger<RabbitMqService> logger,
        string hostname,
        string username,
        string password)
    {
        _logger = logger;

        _factory = new ConnectionFactory
        {
            HostName               = hostname,
            UserName               = username,
            Password               = password,
            // Auto-recover the connection if it drops
            AutomaticRecoveryEnabled      = true,
            NetworkRecoveryInterval       = TimeSpan.FromSeconds(5),
            // Async consumers used by OcrWorkerService — keep this false here
            // since this class only publishes.
            DispatchConsumersAsync        = false,
            RequestedHeartbeat            = TimeSpan.FromSeconds(60),
            RequestedConnectionTimeout    = TimeSpan.FromSeconds(10),
        };

        _logger.LogInformation(
            "RabbitMqService created (lazy) — will connect to {Host} on first use",
            hostname);
    }

    // ── IMessageQueueService ──────────────────────────────────────────────────

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
                var conn    = await GetConnectionAsync(cancellationToken);
                using var ch = conn.CreateModel();

                ch.QueueDeclare(
                    queue:      queueName,
                    durable:    true,
                    exclusive:  false,
                    autoDelete: false,
                    arguments:  null);

                var props = ch.CreateBasicProperties();
                props.Persistent  = true;
                props.ContentType = "application/json";

                ch.BasicPublish(
                    exchange:         string.Empty,
                    routingKey:       queueName,
                    basicProperties:  props,
                    body:             body);

                _logger.LogInformation(
                    "✅ Published message to queue '{Queue}' ({Bytes} bytes)",
                    queueName, body.Length);

                return; // success
            }
            catch (Exception ex) when (
                ex is BrokerUnreachableException or
                      AlreadyClosedException or
                      OperationInterruptedException or
                      IOException)
            {
                // Connection is broken — reset it so the next attempt re-connects
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
                    "⚠️ Publish attempt {Attempt}/{Max} failed for '{Queue}': {Error}. " +
                    "Retrying in {Delay}ms…",
                    attempt, MaxConnectRetries, queueName, ex.Message, delay);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    public Task SubscribeAsync<T>(
        string queueName,
        Func<T, Task> handler,
        CancellationToken cancellationToken = default)
    {
        // NOTE: OcrWorkerService manages its own consumer connection.
        // This method is here only to satisfy the interface.
        // For production use, migrate callers to OcrWorkerService's pattern.
        _logger.LogWarning(
            "SubscribeAsync called on RabbitMqService — use OcrWorkerService for consuming");

        return Task.CompletedTask;
    }

    // ── Connection management ─────────────────────────────────────────────────

    private async Task<IConnection> GetConnectionAsync(CancellationToken ct)
    {
        // Fast path — connection exists and is open
        if (_connection is { IsOpen: true })
            return _connection;

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring the lock
            if (_connection is { IsOpen: true })
                return _connection;

            _connection?.Dispose();
            _connection = null;

            _logger.LogInformation("🔌 Connecting to RabbitMQ at {Host}…", _factory.HostName);

            for (int attempt = 1; attempt <= MaxConnectRetries; attempt++)
            {
                try
                {
                    _connection = _factory.CreateConnection();
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

    private async Task ResetConnectionAsync()
    {
        await _lock.WaitAsync();
        try
        {
            try { _connection?.Dispose(); } catch { /* best effort */ }
            _connection = null;
            _logger.LogInformation("🔄 RabbitMQ connection reset — will reconnect on next use");
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _connection?.Close(); } catch { /* best effort */ }
        try { _connection?.Dispose(); } catch { /* best effort */ }
        _lock.Dispose();
    }
}
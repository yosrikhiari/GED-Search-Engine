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
/// Updated for RabbitMQ.Client v7 async API.
/// </summary>
public class RabbitMqService : IMessageQueueService, IAsyncDisposable, IDisposable
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
            HostName                   = hostname,
            UserName                   = username,
            Password                   = password,
            AutomaticRecoveryEnabled   = true,
            NetworkRecoveryInterval    = TimeSpan.FromSeconds(5),
            RequestedHeartbeat         = TimeSpan.FromSeconds(60),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
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
                var conn = await GetConnectionAsync(cancellationToken);
                var ch   = await conn.CreateChannelAsync(cancellationToken: cancellationToken);
                await using (ch)
                {
                    var props = new BasicProperties
                    {
                        Persistent  = true,
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

    public Task SubscribeAsync<T>(
        string queueName,
        Func<T, Task> handler,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "SubscribeAsync called on RabbitMqService — use OcrWorkerService for consuming");

        return Task.CompletedTask;
    }

    // ── Connection management ─────────────────────────────────────────────────

    private async Task<IConnection> GetConnectionAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true })
            return _connection;

        await _lock.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true })
                return _connection;

            if (_connection != null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            _logger.LogInformation("🔌 Connecting to RabbitMQ at {Host}…", _factory.HostName);

            for (int attempt = 1; attempt <= MaxConnectRetries; attempt++)
            {
                try
                {
                    _connection = await _factory.CreateConnectionAsync(ct);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _connection?.CloseAsync().GetAwaiter().GetResult(); } catch { /* best effort */ }
        try { _connection?.DisposeAsync().GetAwaiter().GetResult(); } catch { /* best effort */ }
        _lock.Dispose();
    }
}
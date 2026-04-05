using RabbitMQ.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace GED.API.Services;

public class RabbitMqQueueInitService : IHostedService
{
    private readonly ILogger<RabbitMqQueueInitService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private IConnection? _connection;

    private const string OcrQueueName = "ocr-queue";
    private const string IndexingQueueName = "indexing-queue";
    private const string DlxName = "ocr-dlx";
    private const string DlqName = "ocr-dead-letter";

    public RabbitMqQueueInitService(
        ILogger<RabbitMqQueueInitService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = "localhost",
                UserName = "admin",
                Password = "admin123",
                AutomaticRecoveryEnabled = true,
                RequestedHeartbeat = TimeSpan.FromSeconds(30),
                RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
            };

            var rabbitHost = Environment.GetEnvironmentVariable("RabbitMQ__Host") ?? "localhost";
            var rabbitUser = Environment.GetEnvironmentVariable("RabbitMQ__Username") ?? "admin";
            var rabbitPass = Environment.GetEnvironmentVariable("RabbitMQ__Password") ?? "admin123";
            var rabbitPortStr = Environment.GetEnvironmentVariable("RabbitMQ__Port") ?? "5672";

            if (int.TryParse(rabbitPortStr, out var rabbitPort))
                factory.Port = rabbitPort;
            factory.HostName = rabbitHost;
            factory.UserName = rabbitUser;
            factory.Password = rabbitPass;

            _logger.LogInformation("Declaring RabbitMQ queues (Host={Host}, Port={Port}, User={User})",
                rabbitHost, rabbitPort, rabbitUser);

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

            try
            {
                await DeclareOcrQueuesAsync(channel, cancellationToken);
                await DeclareIndexingQueuesAsync(channel, cancellationToken);

                _logger.LogInformation("✅ All RabbitMQ queues declared successfully");
            }
            finally
            {
                await channel.CloseAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to declare RabbitMQ queues on startup. Queues will be declared by workers when they start.");
        }
    }

    private async Task DeclareOcrQueuesAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(
            exchange: DlxName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: DlqName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: ct);

        await channel.QueueBindAsync(DlqName, DlxName, OcrQueueName, cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: OcrQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = DlxName,
                ["x-dead-letter-routing-key"] = OcrQueueName,
                ["x-message-ttl"] = 3_600_000,
                ["x-max-length"] = 1000
            },
            cancellationToken: ct);

        _logger.LogInformation("✅ OCR queues declared: {Queue}, {DLQ}", OcrQueueName, DlqName);
    }

    private async Task DeclareIndexingQueuesAsync(IChannel channel, CancellationToken ct)
    {
        const string indexDlxName = "indexing-dlx";
        const string indexDlqName = "indexing-dead-letter";

        await channel.ExchangeDeclareAsync(
            exchange: indexDlxName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: indexDlqName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: ct);

        await channel.QueueBindAsync(indexDlqName, indexDlxName, IndexingQueueName, cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: IndexingQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = indexDlxName,
                ["x-dead-letter-routing-key"] = IndexingQueueName,
                ["x-message-ttl"] = 3_600_000,
                ["x-max-length"] = 1000
            },
            cancellationToken: ct);

        _logger.LogInformation("✅ Indexing queues declared: {Queue}, {DLQ}", IndexingQueueName, indexDlqName);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_connection != null)
        {
            try
            {
                await _connection.CloseAsync(cancellationToken);
            }
            catch
            {
                // Swallow all exceptions during shutdown — connection is being disposed anyway
            }
            try
            {
                _connection.Dispose();
            }
            catch
            {
                // Swallow disposal exceptions
            }
        }
    }
}

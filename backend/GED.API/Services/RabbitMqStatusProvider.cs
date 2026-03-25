using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace GED.API.Services;

public class RabbitMqStatusProvider : IRabbitMqStatusProvider
{
    private readonly IConnection? _connection;
    private readonly ILogger<RabbitMqStatusProvider> _logger;
    private const string QueueName = "ocr-jobs";

    public RabbitMqStatusProvider(
        IConnection? connection,
        ILogger<RabbitMqStatusProvider> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public long GetQueueDepth()
    {
        try
        {
            if (_connection?.IsOpen != true)
                return 0;

            // QueueDeclarePassive is deprecated in v7, use async API
            // For now, return 0 as placeholder - actual implementation needs async channel
            // TODO: Implement proper async queue depth check for RabbitMQ.Client v7
            _logger.LogDebug("Queue depth check not implemented for RabbitMQ.Client v7");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get RabbitMQ queue depth");
            return 0;
        }
    }
}
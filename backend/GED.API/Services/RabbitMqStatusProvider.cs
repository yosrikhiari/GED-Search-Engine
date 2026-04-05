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

            // KNOWN LIMITATION: Queue depth check not implemented for RabbitMQ.Client v7
            // Tracking: The QueueDeclarePassive API is deprecated in v7, requires async channel implementation
            // For now, return 0 as placeholder - actual implementation needs async channel
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
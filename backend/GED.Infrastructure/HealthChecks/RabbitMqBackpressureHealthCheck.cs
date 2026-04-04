using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace GED.Infrastructure.HealthChecks;

public class RabbitMqBackpressureHealthCheck : IHealthCheck
{
    private readonly Func<IConnection?> _connectionFactory;
    private readonly ILogger<RabbitMqBackpressureHealthCheck> _logger;
    private readonly int _warningThreshold;
    private readonly int _criticalThreshold;

    public RabbitMqBackpressureHealthCheck(
        Func<IConnection?> connectionFactory,
        ILogger<RabbitMqBackpressureHealthCheck> logger,
        int warningThreshold = 5000,
        int criticalThreshold = 8000)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _warningThreshold = warningThreshold;
        _criticalThreshold = criticalThreshold;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connection = _connectionFactory();
        if (connection == null || !connection.IsOpen)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ connection is not available");
        }

        try
        {
            var queueMetrics = new List<QueueMetric>();
            var queues = new[] { "ocr-queue", "indexing-queue" };

            foreach (var queueName in queues)
            {
                try
                {
                    using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
                    var declareOk = await channel.QueueDeclarePassiveAsync(queueName, cancellationToken);
                    
                    queueMetrics.Add(new QueueMetric
                    {
                        QueueName = queueName,
                        MessageCount = declareOk.MessageCount,
                        ConsumerCount = declareOk.ConsumerCount
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not get metrics for queue {Queue}", queueName);
                }
            }

            var totalMessages = queueMetrics.Sum(q => (int)q.MessageCount);
            var data = new Dictionary<string, object>();
            
            foreach (var q in queueMetrics)
            {
                data[q.QueueName] = new { messages = q.MessageCount, consumers = q.ConsumerCount };
            }
            data["total_messages"] = totalMessages;

            if (totalMessages >= _criticalThreshold)
            {
                return HealthCheckResult.Unhealthy(
                    $"Critical backpressure: {totalMessages} total messages in queues (threshold: {_criticalThreshold})",
                    data: data);
            }

            if (totalMessages >= _warningThreshold)
            {
                return HealthCheckResult.Degraded(
                    $"High queue depth: {totalMessages} total messages (threshold: {_warningThreshold})",
                    data: data);
            }

            return HealthCheckResult.Healthy($"Queues healthy: {totalMessages} total messages", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Failed to check queue depths", ex);
        }
    }

    private record QueueMetric
    {
        public required string QueueName { get; init; }
        public uint MessageCount { get; init; }
        public uint ConsumerCount { get; init; }
    }
}

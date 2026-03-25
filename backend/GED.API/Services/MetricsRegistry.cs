using System.Diagnostics.Metrics;

namespace GED.API.Services;

public class MetricsRegistry
{
    private readonly Histogram<double> _requestLatency;
    private readonly Histogram<double> _opensearchQueryTime;
    private readonly Histogram<double> _exportSizeBytes;
    private readonly IRabbitMqStatusProvider? _rabbitMqStatus;

    public MetricsRegistry(IMeterFactory meterFactory, IRabbitMqStatusProvider? rabbitMqStatus = null)
    {
        var meter = meterFactory.Create("GED.API");
        
        // Histogram for distribution metrics (latency, duration)
        _requestLatency = meter.CreateHistogram<double>(
            "ged.http.request.latency.ms",
            unit: "ms",
            description: "HTTP request latency in milliseconds");
            
        _opensearchQueryTime = meter.CreateHistogram<double>(
            "ged.opensearch.query.time.ms",
            unit: "ms",
            description: "OpenSearch query execution time");
            
        // ObservableGauge for point-in-time values (queue depth)
        _rabbitMqStatus = rabbitMqStatus;
        if (rabbitMqStatus != null)
        {
            meter.CreateObservableGauge<long>(
                "ged.rabbitmq.queue.depth",
                () => new Measurement<long>(
                    _rabbitMqStatus.GetQueueDepth(),
                    new KeyValuePair<string, object?>("queue", "ocr-jobs")));
        }
            
        _exportSizeBytes = meter.CreateHistogram<double>(
            "ged.export.size.bytes",
            unit: "bytes",
            description: "Document export size in bytes");
    }

    public void RecordRequestLatency(string method, string endpoint, long latencyMs)
    {
        _requestLatency.Record(latencyMs,
            new KeyValuePair<string, object?>("method", method),
            new KeyValuePair<string, object?>("endpoint", endpoint));
    }

    public void RecordOpenSearchQueryTime(string queryType, double timeMs)
    {
        _opensearchQueryTime.Record(timeMs,
            new KeyValuePair<string, object?>("query_type", queryType));
    }

    public void RecordExportSize(double sizeBytes)
    {
        _exportSizeBytes.Record(sizeBytes);
    }
}
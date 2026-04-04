using GED.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

public class IndexingQueueService : IIndexingQueueService
{
    private readonly RabbitMqService _rabbitMq;
    private readonly ILogger<IndexingQueueService> _logger;

    public IndexingQueueService(RabbitMqService rabbitMq, ILogger<IndexingQueueService> logger)
    {
        _rabbitMq = rabbitMq;
        _logger = logger;
    }

    public async Task PublishIndexJobAsync(Guid documentId, IndexingAction action, CancellationToken ct = default)
    {
        var message = new IndexingJobMessage
        {
            JobId = Guid.NewGuid(),
            DocumentId = documentId,
            Action = action,
            CreatedAt = DateTime.UtcNow
        };

        await _rabbitMq.PublishAsync("indexing-queue", message, ct);
        _logger.LogInformation(
            "Published indexing job: jobId={JobId}, docId={DocId}, action={Action}",
            message.JobId, documentId, action);
    }
}

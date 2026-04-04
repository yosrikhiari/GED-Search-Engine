namespace GED.Core.Interfaces;

public interface IIndexingQueueService
{
    Task PublishIndexJobAsync(Guid documentId, IndexingAction action, CancellationToken ct = default);
}

public enum IndexingAction
{
    Index,
    Reindex,
    Delete,
    UpdateAcl
}

public class IndexingJobMessage
{
    public Guid JobId { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public IndexingAction Action { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
    
    /// <summary>
    /// Correlation ID for distributed tracing across async workers.
    /// </summary>
    public string? CorrelationId { get; set; }
}

public class IndexingJobSimpleMessage
{
    public Guid DocumentId { get; set; }
    public bool HasExtractedText { get; set; }
}

using System.Text.Json.Serialization;

namespace GED.Core.Models;

public class PipelineEvent
{
    [JsonPropertyName("@timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("pipeline_stage")]
    public string PipelineStage { get; set; } = string.Empty;

    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("correlation_id")]
    public string CorrelationId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    [JsonPropertyName("service_name")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("file_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileName { get; set; }

    [JsonPropertyName("file_size_bytes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? FileSizeBytes { get; set; }

    [JsonPropertyName("content_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentType { get; set; }

    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }

    [JsonPropertyName("user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; set; }

    [JsonPropertyName("file_hash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileHash { get; set; }

    [JsonPropertyName("duplicate_detected")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DuplicateDetected { get; set; }

    [JsonPropertyName("text_length_chars")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TextLengthChars { get; set; }

    [JsonPropertyName("extraction_method")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExtractionMethod { get; set; }

    [JsonPropertyName("description_source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DescriptionSource { get; set; }

    [JsonPropertyName("initial_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InitialStatus { get; set; }

    [JsonPropertyName("outbox_message_created")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? OutboxMessageCreated { get; set; }

    [JsonPropertyName("queue_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? QueueName { get; set; }

    [JsonPropertyName("retry_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RetryCount { get; set; }

    [JsonPropertyName("outbox_backlog_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OutboxBacklogCount { get; set; }

    [JsonPropertyName("ocr_skipped")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? OcrSkipped { get; set; }

    [JsonPropertyName("ocr_confidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? OcrConfidence { get; set; }

    [JsonPropertyName("page_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PageCount { get; set; }

    [JsonPropertyName("llm_cleaning_used")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LlmCleaningUsed { get; set; }

    [JsonPropertyName("tags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("chunk_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ChunkCount { get; set; }

    [JsonPropertyName("embedding_model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EmbeddingModel { get; set; }

    [JsonPropertyName("embedding_dimension")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EmbeddingDimension { get; set; }

    [JsonPropertyName("error_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorType { get; set; }

    [JsonPropertyName("error_message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }
}

public static class PipelineStages
{
    public const string Upload = "upload";
    public const string FileStorage = "file_storage";
    public const string Ingestion = "ingestion";
    public const string DbPersist = "db_persist";
    public const string OutboxRelay = "outbox_relay";
    public const string OcrWorker = "ocr_worker";
    public const string IndexingWorker = "indexing_worker";
}

public static class PipelineStatuses
{
    public const string Started = "started";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}
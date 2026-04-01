namespace GED.Infrastructure.Data;

/// <summary>
/// Outbox Pattern (ByteByteGo: Reliable message delivery).
///
/// Problem solved: When a document is saved but RabbitMQ is temporarily down,
/// the OCR job is silently lost — the document exists in DB but OCR never runs.
///
/// Solution: Write the OCR job to the DB in the SAME transaction as the document.
/// A relay worker (OutboxRelayService) picks it up and publishes to RabbitMQ.
/// Guarantees at-least-once delivery regardless of broker availability.
///
/// Flow:
///   1. DocumentsController saves document + OutboxMessage in one SaveChangesAsync()
///   2. OutboxRelayService polls every 5s, publishes unprocessed messages
///   3. Sets ProcessedAt when published successfully
///   4. If publish fails, increments RetryCount (max 5 attempts)
/// </summary>
public class OutboxMessage
{
    public Guid      Id          { get; set; } = Guid.NewGuid();

    /// <summary>Message type, e.g. "OcrJob". Used by relay to route to correct queue.</summary>
    public string    Type        { get; set; } = string.Empty;

    /// <summary>JSON payload matching the RabbitMQ message format.</summary>
    public string    Payload     { get; set; } = string.Empty;

    /// <summary>When this message was created (same transaction as the document).</summary>
    public DateTime  CreatedAt   { get; set; } = DateTime.UtcNow;

    /// <summary>Set by relay after successful publish. Null = not yet published.</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Set by worker after successful processing (ack). Null = not yet acknowledged.</summary>
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>Last error message if publish failed.</summary>
    public string?   Error       { get; set; }

    /// <summary>How many publish attempts have failed. Relay stops at 5.</summary>
    public int       RetryCount  { get; set; } = 0;
}
namespace GED.Core.Models;

public class OcrJob
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public OcrStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Language { get; set; } = "eng";
    public string? ExtractedText { get; set; }
    public int PageCount { get; set; }
    public float Confidence { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum OcrStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

public class OcrResult
{
    public Guid JobId { get; set; }
    public Guid DocumentId { get; set; }
    public bool Success { get; set; }
    public string? ExtractedText { get; set; }
    public int PageCount { get; set; }
    public List<PageOcrResult> Pages { get; set; } = new();
    public float AverageConfidence { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PageOcrResult
{
    public int PageNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
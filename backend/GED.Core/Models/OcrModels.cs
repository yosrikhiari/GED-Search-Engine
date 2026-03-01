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

    // ── NEW: populated when status >= OcrStatus.TextExtracted ────────────────
    /// <summary>
    /// Character count from Tesseract (raw, before LLM cleaning).
    /// Set as soon as Tesseract finishes, so the frontend can show progress
    /// even while Ollama cleaning is still running.
    /// </summary>
    public int RawTextLength { get; set; }

    /// <summary>Human-readable label for the current pipeline stage.</summary>
    public string? StageLabel { get; set; }
}

/// <summary>
/// OCR pipeline stages, in order of progression.
///
///   Pending        → job is queued, worker hasn't started yet
///   Processing     → Tesseract is running
///   TextExtracted  → *** NEW *** Tesseract finished; LLM cleaning not yet done
///                    IsOcrProcessed = true at this point so polls resolve fast.
///   LlmCleaning   → *** NEW *** Ollama cleaning + date extraction in progress
///   Completed      → full pipeline done (cleaned text + metadata stored)
///   Failed         → unrecoverable error at any stage
/// </summary>
public enum OcrStatus
{
    Pending        = 0,
    Processing     = 1,
    TextExtracted  = 2,   // ← NEW: Tesseract done, LLM pending
    LlmCleaning    = 3,   // ← NEW: Ollama running
    Completed      = 4,   // ← was 2
    Failed         = 5    // ← was 3
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
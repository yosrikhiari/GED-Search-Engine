namespace GED.Core.Models;

public class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public SearchType SearchType { get; set; } = SearchType.Natural;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public List<string>? Categories { get; set; }
    public List<string>? Tags { get; set; }
    public List<Guid>? DocumentIds { get; set; }
    public List<string>? ContentTypes { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Dictionary<string, object>? Filters { get; set; }
    public string? UserId       { get; set; }  // Guid string
    public string? UserRole     { get; set; }  // "Admin" | "Manager" | "User" | "ReadOnly"
    public List<string>? UserAllowedCategories { get; set; }
    public SortField SortBy { get; set; } = SortField.Relevance;
    public bool SortDescending { get; set; } = true;
    public bool IncludeOcrContent { get; set; } = true;
    public bool IncludeSuggestions { get; set; } = false;
    public int? MinScore { get; set; }
}

public enum SearchType
{
    Natural,
    Exact,
    Fuzzy,
    Wildcard,
    Advanced
}

public enum SortField
{
    Relevance,
    CreatedDate,
    ModifiedDate,
    Title,
    FileSize
}

public class SearchResult
{
    public int TotalResults { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<DocumentSearchHit> Documents { get; set; } = new();
    public Dictionary<string, List<FacetValue>>? Facets { get; set; }
    public List<string>? DidYouMean { get; set; }
    public List<DocumentSuggestion>? Suggestions { get; set; }
    public long SearchTimeMs { get; set; }
    public string? ProcessedQuery { get; set; }

    // ── Multilingual NLP metadata ─────────────────────────────────────────────
    // Returned to the frontend in a single response — no second API call needed.

    /// <summary>
    /// False when the query has no meaningful search terms in any language.
    /// Frontend should show "Please enter a proper search term." and not display results.
    /// </summary>
    public bool IsUnderstood { get; set; } = true;

    /// <summary>ISO 639-1 code: "en", "fr", "ar", or "unknown".</summary>
    public string? DetectedLanguage { get; set; }

    /// <summary>
    /// Human-readable summary of what the NLP pipeline understood, for the info banner.
    /// Example: "Factures · PDF · depuis 2024-01-01"
    /// </summary>
    public string? NlpSummary { get; set; }

    /// <summary>Which search modes contributed to this result set.</summary>
    public SearchMode SearchMode { get; set; } = SearchMode.BM25;

    // ── Pending documents (not yet indexed) ───────────────────────────────────
    /// <summary>
    /// Documents that exist in the database but are not yet indexed in OpenSearch.
    /// These are shown in a separate "En cours de traitement" section on the frontend.
    /// </summary>
    public List<DocumentSearchHit> PendingDocuments { get; set; } = new();

    /// <summary>Count of documents waiting in the queue (for UI badge).</summary>
    public int PendingCount { get; set; }
}

/// <summary>Which scoring strategy was used for this result set.</summary>
public enum SearchMode
{
    BM25,        // Keyword-only (Ollama unavailable)
    Semantic,    // Vector-only (no keyword match)
    Hybrid       // BM25 + semantic combined
}

public class DocumentSearchHit
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string? Category { get; set; }
    public List<string>? Tags { get; set; }
    public float Score { get; set; }
    public List<string>? Highlights { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public string? OcrText { get; set; }
    public string? ExtractedText { get; set; }
    public string Status { get; set; } = "Indexed";
    public bool IsOcrProcessed { get; set; }
    public bool IsFullyProcessed { get; set; }

    // ── Pending document fields ────────────────────────────────────────────────
    /// <summary>
    /// Human-readable processing stage for pending documents.
    /// Examples: "En attente de traitement", "OCR en cours", "Indexation en attente"
    /// </summary>
    public string? ProcessingStage { get; set; }

    /// <summary>
    /// Position in the processing queue (1-indexed).
    /// Null for indexed documents or when queue position is not applicable.
    /// </summary>
    public int? QueuePosition { get; set; }

    /// <summary>
    /// The actual DocumentStatus enum value for internal use.
    /// Maps to ProcessingStage for display.
    /// </summary>
    public string? DocumentStatus { get; set; }

    /// <summary>
    /// OCR status stage label from the pipeline.
    /// </summary>
    public string? OcrStageLabel { get; set; }

    /// <summary>
    /// Whether an outbox message has been published to RabbitMQ.
    /// </summary>
    public bool IsQueued { get; set; }
}

public class FacetValue
{
    public string Value { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class DocumentSuggestion
{
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public float SimilarityScore { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class NaturalLanguageQuery
{
    public string OriginalQuery { get; set; } = string.Empty;
    public string ProcessedQuery { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = new();
    public List<string> Entities { get; set; } = new();
    public QueryIntent Intent { get; set; }
    public Dictionary<string, string>? ExtractedFilters { get; set; }

    // ── Language awareness ────────────────────────────────────────────────────

    /// <summary>ISO 639-1 code detected purely from character ranges/heuristics. No LLM needed.</summary>
    public string DetectedLanguage { get; set; } = "unknown";

    /// <summary>
    /// True when the query contains at least one meaningful search token.
    /// False for gibberish, single-char, or pure stop-word queries.
    /// </summary>
    public bool IsUnderstood { get; set; } = true;
}

public enum QueryIntent
{
    Search,
    Find,
    List,
    Filter,
    Compare,
    Summarize
}
namespace GED.Core.Models;

// ── Request ───────────────────────────────────────────────────────────────────

public class RagRequest
{
    /// <summary>The natural-language question from the user.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Optional: restrict search to these categories.</summary>
    public List<string>? Categories { get; set; }

    /// <summary>Optional: restrict RAG search to these specific document IDs.</summary>
    public List<Guid>? DocumentIds { get; set; }

    /// <summary>Optional: restrict search to these MIME types.</summary>
    public List<string>? ContentTypes { get; set; }

    /// <summary>Optional: date range filter (start).</summary>
    public DateTime? FromDate { get; set; }

    /// <summary>Optional: date range filter (end).</summary>
    public DateTime? ToDate { get; set; }

    /// <summary>Response language: "fr" (default), "en", "ar".</summary>
    public string Language { get; set; } = "fr";

    [System.Text.Json.Serialization.JsonIgnore]
    public string? Username { get; set; }
}

// ── Response ──────────────────────────────────────────────────────────────────

public class RagResponse
{
    /// <summary>The original user query.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>The AI-generated synthetic answer.</summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>Source documents used to build the answer.</summary>
    public List<RagSource> Sources { get; set; } = new();

    /// <summary>Total time for the RAG pipeline (search + generation).</summary>
    public long SearchTimeMs { get; set; }

    /// <summary>Total number of documents in the index that matched.</summary>
    public int TotalDocumentsSearched { get; set; }

    /// <summary>True if retrieved chunks had sufficient confidence. False = low-confidence answer.</summary>
    public bool IsConfident { get; set; } = true;

    /// <summary>LLM model used for generation.</summary>
    public string? ModelUsed { get; set; }
}

/// <summary>
/// A single document used as a source in the RAG response.
/// </summary>
public class RagSource
{
    public Guid DocumentId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Category { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    /// <summary>Document content date (e.g. contract effective date).</summary>
    public DateTime? DocumentDate { get; set; }

    /// <summary>Upload date.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Relevance score from OpenSearch (0–1).</summary>
    public float RelevanceScore { get; set; }

    /// <summary>The text excerpt that was fed to the LLM as context.</summary>
    public string Excerpt { get; set; } = string.Empty;

    /// <summary>OpenSearch highlights for this document.</summary>
    public List<string> Highlights { get; set; } = new();

    // Set by the controller from the JWT — never comes from the request body
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Username { get; set; }

}

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

    // ── Identity fields — set by the controller, NEVER from the request body ──
    // [JsonIgnore] ensures these are never deserialized from client input,
    // preventing privilege escalation.

    /// <summary>Username resolved from the session cookie — used for category-level ACL.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Username { get; set; }

    /// <summary>User Guid as a string — forwarded to the OpenSearch allowedUserIds filter.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? UserId { get; set; }

    /// <summary>"Admin" | "Manager" | "User" | "ReadOnly" — Admin bypasses all ACL filters.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? UserRole { get; set; }

    /// <summary>
    /// Categories the user is allowed to access (from AppUser.AllowedCategories).
    /// Null = no category restriction beyond explicit document grants.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<string>? UserAllowedCategories { get; set; }
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

    /// <summary>Total number of documents that matched in the index.</summary>
    public int TotalDocumentsSearched { get; set; }

    /// <summary>True if retrieved chunks had sufficient confidence. False = low-confidence answer.</summary>
    public bool IsConfident { get; set; } = true;

    /// <summary>LLM model used for generation.</summary>
    public string? ModelUsed { get; set; }
}

/// <summary>A single document used as a source in the RAG response.</summary>
public class RagSource
{
    public Guid      DocumentId    { get; set; }
    public string    Title         { get; set; } = string.Empty;
    public string?   Category      { get; set; }
    public string    FileName      { get; set; } = string.Empty;
    public string    ContentType   { get; set; } = string.Empty;
    public DateTime? DocumentDate  { get; set; }
    public DateTime  CreatedAt     { get; set; }
    public float     RelevanceScore { get; set; }
    public string    Excerpt       { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = new();
}
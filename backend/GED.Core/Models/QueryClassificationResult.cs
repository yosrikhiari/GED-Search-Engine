namespace GED.Core.Models;

/// <summary>
/// Result of query classification containing the detected query type,
/// confidence score, and recommended retrieval parameters.
/// </summary>
public class QueryClassificationResult
{
    /// <summary>
    /// The classified query type.
    /// </summary>
    public string QueryType { get; set; } = "factual";

    /// <summary>
    /// Confidence score from 0.0 to 1.0.
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Recommended number of chunks to retrieve (null = use default).
    /// </summary>
    public int? RecommendedTopK { get; set; }

    /// <summary>
    /// Recommended confidence threshold for filtering (null = use default).
    /// </summary>
    public float? RecommendedConfidenceThreshold { get; set; }

    /// <summary>
    /// Whether the classification was ambiguous or low-confidence.
    /// </summary>
    public bool IsAmbiguous { get; set; }

    /// <summary>
    /// Original type before fallback (for monitoring misclassifications).
    /// </summary>
    public string? OriginalType { get; set; }

    /// <summary>
    /// Keywords matched during classification (for debugging).
    /// </summary>
    public List<string> MatchedKeywords { get; set; } = new();

    /// <summary>
    /// Patterns matched during classification (for debugging).
    /// </summary>
    public List<string> MatchedPatterns { get; set; } = new();
}

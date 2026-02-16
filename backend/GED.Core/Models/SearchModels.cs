namespace GED.Core.Models;

public class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    
    public SearchType SearchType { get; set; } = SearchType.Natural;
    
    public int Page { get; set; } = 1;
    
    public int PageSize { get; set; } = 20;
    
    public List<string>? Categories { get; set; }
    
    public List<string>? Tags { get; set; }
    
    public List<string>? ContentTypes { get; set; }
    
    public DateTime? FromDate { get; set; }
    
    public DateTime? ToDate { get; set; }
    
    public Dictionary<string, object>? Filters { get; set; }
    
    public SortField SortBy { get; set; } = SortField.Relevance;
    
    public bool SortDescending { get; set; } = true;
    
    public bool IncludeOcrContent { get; set; } = true;
    
    public bool IncludeSuggestions { get; set; } = false;
    
    public int? MinScore { get; set; }
}

public enum SearchType
{
    Natural,      // Natural language understanding
    Exact,        // Exact phrase match
    Fuzzy,        // Fuzzy matching
    Wildcard,     // Wildcard search
    Advanced      // Boolean operators
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
}

public class DocumentSearchHit
{
    public Guid Id { get; set; }
    
    public string Title { get; set; } = string.Empty;
    
    public string? Description { get; set; }
    
    public string FileName { get; set; } = string.Empty;
    
    public string ContentType { get; set; } = string.Empty;
    
    public long FileSize { get; set; }
    
    public DateTime CreatedAt { get; set; }  // Upload date
    
    public DateTime? DocumentDate { get; set; }  // ⭐ NEW: Document content date
    
    public DateTime? ModifiedAt { get; set; }
    
    public string? Category { get; set; }
    
    public List<string>? Tags { get; set; }
    
    public float Score { get; set; }
    
    public List<string>? Highlights { get; set; }
    
    public Dictionary<string, object>? Metadata { get; set; }
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
}

public enum QueryIntent
{
    Search,
    Find,
    List,
    Filter,
    Compare,
    Unknown
}

using OpenSearch.Client;
using OpenSearch.Net;
using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Logging;
using CoreSearchRequest = GED.Core.Models.SearchRequest;

namespace GED.Infrastructure.Services;

public class OpenSearchService : ISearchService
{
    private readonly IOpenSearchClient _client;
    private readonly INlpService _nlpService;
    private readonly ILogger<OpenSearchService> _logger;
    private const string DocumentIndex = "ged-documents";

    public OpenSearchService(
        IOpenSearchClient client,
        INlpService nlpService,
        ILogger<OpenSearchService> logger)
    {
        _client = client;
        _nlpService = nlpService;
        _logger = logger;
    }

    public async Task<SearchResult> SearchAsync(CoreSearchRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var startTime = DateTime.UtcNow;

            // Process natural language query if needed
            string processedQuery = request.Query;
            NaturalLanguageQuery? nlQuery = null;

            if (request.SearchType == GED.Core.Models.SearchType.Natural && !string.IsNullOrWhiteSpace(request.Query))
            {
                nlQuery = await _nlpService.UnderstandQueryAsync(request.Query, cancellationToken);
                processedQuery = nlQuery.ProcessedQuery;
                _logger.LogInformation("NLP processed query: '{Original}' -> '{Processed}', Keywords: {Keywords}", 
                    request.Query, processedQuery, string.Join(", ", nlQuery.Keywords));
                
                // ⭐ APPLY NLP-EXTRACTED FILTERS TO REQUEST ⭐
                if (nlQuery.ExtractedFilters != null && nlQuery.ExtractedFilters.Any())
                {
                    _logger.LogInformation("Applying {Count} NLP-extracted filters", nlQuery.ExtractedFilters.Count);
                    
                    // Apply date filters
                    if (nlQuery.ExtractedFilters.ContainsKey("fromDate"))
                    {
                        request.FromDate = DateTime.Parse(nlQuery.ExtractedFilters["fromDate"]);
                        _logger.LogInformation("✅ Applied FromDate filter: {Date}", request.FromDate);
                    }
                    if (nlQuery.ExtractedFilters.ContainsKey("toDate"))
                    {
                        request.ToDate = DateTime.Parse(nlQuery.ExtractedFilters["toDate"]);
                        _logger.LogInformation("✅ Applied ToDate filter: {Date}", request.ToDate);
                    }
                    
                    // Apply file type filter
                    if (nlQuery.ExtractedFilters.ContainsKey("filetype"))
                    {
                        var fileType = nlQuery.ExtractedFilters["filetype"];
                        var contentType = MapFileTypeToContentType(fileType);
                        if (!string.IsNullOrEmpty(contentType))
                        {
                            request.ContentTypes = new List<string> { contentType };
                            _logger.LogInformation("✅ Applied ContentType filter: {Type}", contentType);
                        }
                    }
                }
                
                // Detect generic "show all" type queries
                var lowerQuery = request.Query.ToLower().Trim();
                var genericPhrases = new[] { "all documents", "show all", "list all", "get all", "find all" };
                
                if (genericPhrases.Any(phrase => lowerQuery.Contains(phrase)) || 
                    (!nlQuery.Keywords.Any() && lowerQuery.Split(' ').All(w => w.Length <= 4)))
                {
                    // This is a generic "show everything" query - clear the search text
                    processedQuery = string.Empty;
                    _logger.LogInformation("Detected generic 'show all' query - returning all documents");
                }
            }

            // Build OpenSearch query
            var searchResponse = await _client.SearchAsync<DocumentIndexModel>(s => s
                .Index(DocumentIndex)
                .From((request.Page - 1) * request.PageSize)
                .Size(request.PageSize)
                .Query(q => BuildQuery(q, processedQuery, request, nlQuery))
                .Sort(ss => BuildSort(ss, request.SortBy, request.SortDescending))
                .Highlight(h => h
                    .Fields(
                        f => f.Field(doc => doc.Title).NumberOfFragments(0),
                        f => f.Field(doc => doc.Description).NumberOfFragments(3).FragmentSize(150),
                        f => f.Field(doc => doc.ExtractedText).NumberOfFragments(3).FragmentSize(150),
                        f => f.Field(doc => doc.OcrText).NumberOfFragments(3).FragmentSize(150)
                    )
                )
                .Aggregations(a => BuildAggregations(a))
                .MinScore(request.MinScore ?? 0),
                cancellationToken
            );

            if (!searchResponse.IsValid)
            {
                _logger.LogError("OpenSearch query failed: {Error}", searchResponse.DebugInformation);
                throw new Exception($"Search failed: {searchResponse.ServerError?.Error?.Reason}");
            }

            _logger.LogInformation("Search returned {Count} results out of {Total} total", 
                searchResponse.Documents.Count, searchResponse.Total);


            // Map hits to search results
            var documents = searchResponse.Hits.Select(hit => MapToSearchHit(hit)).ToList();

            // Normalize scores to 0-100% range
            if (documents.Any())
            {
                var maxScore = documents.Max(d => d.Score);
                if (maxScore > 0)
                {
                    foreach (var doc in documents)
                    {
                        doc.Score = doc.Score / maxScore; // Normalize to 0-1.0 range
                    }
                }
            }

            var result = new SearchResult
            {
                TotalResults = (int)searchResponse.Total,
                Page = request.Page,
                PageSize = request.PageSize,
                TotalPages = (int)Math.Ceiling((double)searchResponse.Total / request.PageSize),
                Documents = documents,
                Facets = ExtractFacets(searchResponse.Aggregations),
                SearchTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds,
                ProcessedQuery = processedQuery
            };

            // Get related document suggestions if requested
            if (request.IncludeSuggestions && result.Documents.Any())
            {
                result.Suggestions = await GetRelatedDocumentsAsync(
                    result.Documents.First().Id,
                    5,
                    cancellationToken
                );
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing search");
            throw;
        }
    }

// This is the CRITICAL fix for the BuildQuery method in OpenSearchService.cs
// Replace the BuildQuery method (around line 105) with this version

private QueryContainer BuildQuery(
    QueryContainerDescriptor<DocumentIndexModel> q,
    string query,
    CoreSearchRequest request,
    NaturalLanguageQuery? nlQuery)
{
    var textSearchQueries = new List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>>();
    var filterQueries = new List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>>();

    // ========== TEXT SEARCH QUERIES (for scoring) ==========
    
    // Main text query - ONLY add if we have actual search terms after processing
    if (!string.IsNullOrWhiteSpace(query))
    {
        // Strategy 1: Multi-match with fuzziness (handles typos and variations)
        textSearchQueries.Add(sq => sq.MultiMatch(m => m
            .Query(query)
            .Fields(f => f
                .Field(doc => doc.Title, 5.0)
                .Field(doc => doc.Category, 8.0)  // Boost category higher
                .Field(doc => doc.FileName, 3.0)
                .Field(doc => doc.Description, 2.0)
                .Field(doc => doc.ExtractedText, 2.0)
                .Field(doc => doc.OcrText, 1.0)
            )
            .Type(TextQueryType.BestFields)
            .Fuzziness(Fuzziness.Auto)  // Handles singular/plural
            .Operator(Operator.Or)
        ));

        // Strategy 2: Wildcard on category (case-insensitive prefix matching)
        textSearchQueries.Add(sq => sq.Wildcard(w => w
            .Field(doc => doc.Category)
            .Value($"{query.ToLower()}*")
            .CaseInsensitive(true)
            .Boost(10.0)
        ));

        // Strategy 3: Wildcard on title (case-insensitive partial matching)
        textSearchQueries.Add(sq => sq.Wildcard(w => w
            .Field(doc => doc.Title)
            .Value($"*{query.ToLower()}*")
            .CaseInsensitive(true)
            .Boost(4.0)
        ));

        // Strategy 4: Match on extracted text
        textSearchQueries.Add(sq => sq.Match(m => m
            .Field(doc => doc.ExtractedText)
            .Query(query)
            .Fuzziness(Fuzziness.Auto)
            .Boost(2.0)
        ));
    }

    // ========== FILTER QUERIES (hard exclusions, no scoring) ==========
    
    // ⭐ CRITICAL: Status filter - Only show indexed documents
    filterQueries.Add(sq => sq.Term(t => t.Field("status").Value("Indexed")));
    
    // ⭐ CRITICAL: Content type filter - HARD EXCLUSION
    if (request.ContentTypes?.Any() == true)
    {
        _logger.LogInformation("🔒 Applying HARD contentType filter: {Types}", 
            string.Join(", ", request.ContentTypes));
        
        filterQueries.Add(ctq => ctq.Terms(t => t
            .Field(doc => doc.ContentType)
            .Terms(request.ContentTypes)
        ));
    }

    // Category filter
    if (request.Categories?.Any() == true)
    {
        _logger.LogInformation("🔒 Applying category filter: {Categories}", 
            string.Join(", ", request.Categories));
        
        filterQueries.Add(cq => cq.Terms(t => t
            .Field("category.keyword")
            .Terms(request.Categories)
        ));
    }

    // Tags filter
    if (request.Tags?.Any() == true)
    {
        _logger.LogInformation("🔒 Applying tags filter: {Tags}", 
            string.Join(", ", request.Tags));
        
        filterQueries.Add(tq => tq.Terms(t => t
            .Field(doc => doc.Tags)
            .Terms(request.Tags)
        ));
    }

    // ⭐ CRITICAL: Date range filter - HARD EXCLUSION
    if (request.FromDate.HasValue || request.ToDate.HasValue)
    {
        _logger.LogInformation("🔒 Applying date range filter: {From} to {To}", 
            request.FromDate?.ToString("yyyy-MM-dd") ?? "start", 
            request.ToDate?.ToString("yyyy-MM-dd") ?? "end");
        
        filterQueries.Add(dq => dq.DateRange(dr => dr
            .Field(doc => doc.CreatedAt)
            .GreaterThanOrEquals(request.FromDate)
            .LessThanOrEquals(request.ToDate)
        ));
    }

    // ========== BUILD FINAL QUERY ==========
    
    _logger.LogInformation("📊 Query structure: {TextQueries} text queries, {FilterQueries} filter queries",
        textSearchQueries.Count, filterQueries.Count);
    
    // Build bool query with proper Filter vs Must separation
    return q.Bool(b =>
    {
        var boolQuery = b;
        
        // ⭐ CRITICAL: Apply filters in Filter clause (not Must)
        // Filter clause = hard exclusion, no scoring, cacheable
        if (filterQueries.Any())
        {
            boolQuery = boolQuery.Filter(filterQueries.ToArray());
        }
        
        // Apply text search in Must clause (affects scoring)
        if (textSearchQueries.Any())
        {
            boolQuery = boolQuery.Must(m => m.Bool(sb =>
            {
                var shouldBool = sb;
                foreach (var textQuery in textSearchQueries)
                {
                    shouldBool = shouldBool.Should(textQuery);
                }
                return shouldBool.MinimumShouldMatch(1);
            }));
        }
        else
        {
            // No text search - just return all documents matching filters
            boolQuery = boolQuery.Must(m => m.MatchAll());
        }
        
        return boolQuery;
    });
}
    private SortDescriptor<DocumentIndexModel> BuildSort(
        SortDescriptor<DocumentIndexModel> sort,
        SortField sortBy,
        bool descending)
    {
        var order = descending ? SortOrder.Descending : SortOrder.Ascending;

        return sortBy switch
        {
            SortField.Relevance => sort.Descending(SortSpecialField.Score),
            SortField.CreatedDate => sort.Field(doc => doc.CreatedAt, order),
            SortField.ModifiedDate => sort.Field(doc => doc.ModifiedAt, order),
            SortField.Title => sort.Field("title.keyword", order),
            SortField.FileSize => sort.Field(doc => doc.FileSize, order),
            _ => sort.Descending(SortSpecialField.Score)
        };
    }

    private AggregationContainerDescriptor<DocumentIndexModel> BuildAggregations(
        AggregationContainerDescriptor<DocumentIndexModel> agg)
    {
        return agg
            .Terms("categories", t => t.Field("category.keyword").Size(10))
            .Terms("content_types", t => t.Field(doc => doc.ContentType).Size(10))
            .Terms("tags", t => t.Field(doc => doc.Tags).Size(20))
            .DateHistogram("created_dates", d => d
                .Field(doc => doc.CreatedAt)
                .CalendarInterval(DateInterval.Month)
            );
    }

    private DocumentSearchHit MapToSearchHit(IHit<DocumentIndexModel> hit)
    {
        var doc = hit.Source;
        var highlights = new List<string>();

        if (hit.Highlight != null)
        {
            foreach (var highlight in hit.Highlight.Values)
            {
                highlights.AddRange(highlight);
            }
        }

        return new DocumentSearchHit
        {
            Id = doc.Id,
            Title = doc.Title,
            Description = doc.Description,
            FileName = doc.FileName,
            ContentType = doc.ContentType,
            FileSize = doc.FileSize,
            CreatedAt = doc.CreatedAt,
            ModifiedAt = doc.ModifiedAt,
            Category = doc.Category,
            Tags = doc.Tags,
            Score = (float)(hit.Score ?? 0),
            Highlights = highlights.Any() ? highlights : null,
            Metadata = doc.Metadata
        };
    }

    private Dictionary<string, List<FacetValue>> ExtractFacets(IReadOnlyDictionary<string, IAggregate> aggregations)
    {
        var facets = new Dictionary<string, List<FacetValue>>();

        foreach (var agg in aggregations)
        {
            if (agg.Value is BucketAggregate bucketAgg)
            {
                facets[agg.Key] = bucketAgg.Items
                    .OfType<KeyedBucket<object>>()
                    .Select(b => new FacetValue
                    {
                        Value = b.Key.ToString() ?? "",
                        Count = (int)(b.DocCount ?? 0)
                    })
                    .ToList();
            }
        }

        return facets;
    }

    public async Task<List<DocumentSuggestion>> GetRelatedDocumentsAsync(
        Guid documentId,
        int count = 5,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var document = await GetDocumentFromIndexAsync(documentId, cancellationToken);
            if (document == null) return new List<DocumentSuggestion>();

            var response = await _client.SearchAsync<DocumentIndexModel>(s => s
                .Index(DocumentIndex)
                .Size(count + 1)
                .Query(q => q.MoreLikeThis(mlt => mlt
                    .Fields(f => f
                        .Field(doc => doc.Title)
                        .Field(doc => doc.Description)
                        .Field(doc => doc.ExtractedText)
                        .Field(doc => doc.Tags)
                    )
                    .Like(l => l.Document(d => d.Id(documentId.ToString())))
                    .MinTermFrequency(1)
                    .MinDocumentFrequency(1)
                )),
                cancellationToken
            );

            return response.Documents
                .Where(d => d.Id != documentId)
                .Take(count)
                .Select(d => new DocumentSuggestion
                {
                    DocumentId = d.Id,
                    Title = d.Title,
                    SimilarityScore = 0.8f,
                    Reason = "Similar content and tags"
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting related documents for {DocumentId}", documentId);
            return new List<DocumentSuggestion>();
        }
    }

    public async Task<NaturalLanguageQuery> ProcessNaturalLanguageQueryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        return await _nlpService.UnderstandQueryAsync(query, cancellationToken);
    }

    public async Task<bool> IndexDocumentAsync(Document document, CancellationToken cancellationToken = default)
    {
        try
        {
            var indexModel = MapToIndexModel(document);

            _logger.LogInformation("Indexing document {DocumentId}: Status={Status}, Title={Title}, Category={Category}",
                document.Id, indexModel.Status, indexModel.Title, indexModel.Category);

            var response = await _client.IndexAsync(indexModel, i => i
                .Index(DocumentIndex)
                .Id(document.Id.ToString()),
                cancellationToken
            );

            if (response.IsValid)
            {
                // Force refresh to make document immediately searchable
                await _client.Indices.RefreshAsync(DocumentIndex, r => r, cancellationToken);
                _logger.LogInformation("✅ Document {DocumentId} indexed successfully", document.Id);
                return true;
            }
            else
            {
                _logger.LogError("❌ Failed to index document {DocumentId}: {Error}",
                    document.Id, response.DebugInformation);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing document {DocumentId}", document.Id);
            return false;
        }
    }

    public async Task<bool> UpdateDocumentIndexAsync(Document document, CancellationToken cancellationToken = default)
    {
        return await IndexDocumentAsync(document, cancellationToken);
    }

    public async Task<bool> DeleteDocumentIndexAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.DeleteAsync<DocumentIndexModel>(
                documentId.ToString(),
                d => d.Index(DocumentIndex),
                cancellationToken
            );

            return response.IsValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document index {DocumentId}", documentId);
            return false;
        }
    }

    public async Task<bool> BulkIndexDocumentsAsync(
        IEnumerable<Document> documents,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexModels = documents.Select(MapToIndexModel);

            var response = await _client.BulkAsync(b => b
                .Index(DocumentIndex)
                .IndexMany(indexModels),
                cancellationToken
            );

            if (response.IsValid && !response.ItemsWithErrors.Any())
            {
                // Force refresh after bulk operation
                await _client.Indices.RefreshAsync(DocumentIndex, r => r, cancellationToken);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk indexing documents");
            return false;
        }
    }

    private async Task<DocumentIndexModel?> GetDocumentFromIndexAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var response = await _client.GetAsync<DocumentIndexModel>(
            documentId.ToString(),
            g => g.Index(DocumentIndex),
            cancellationToken
        );

        return response.Found ? response.Source : null;
    }

    private DocumentIndexModel MapToIndexModel(Document document)
    {
        return new DocumentIndexModel
        {
            Id = document.Id,
            Title = document.Title,
            Description = document.Description,
            FileName = document.FileName,
            ContentType = document.ContentType,
            FileSize = document.FileSize,
            CreatedAt = document.CreatedAt,
            ModifiedAt = document.ModifiedAt,
            Status = document.Status.ToString(),
            ExtractedText = document.ExtractedText,
            OcrText = document.OcrText,
            Tags = document.Tags,
            Category = document.Category,
            Metadata = document.Metadata
        };
    }

    // ⭐ NEW HELPER METHOD TO MAP FILE TYPES TO CONTENT TYPES ⭐
    private string MapFileTypeToContentType(string fileType)
    {
        return fileType.ToLower() switch
        {
            "pdf" => "application/pdf",
            "doc" => "application/msword",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "xls" => "application/vnd.ms-excel",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "txt" => "text/plain",
            _ => ""
        };
    }
}

// Status should be string in index
public class DocumentIndexModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string Status { get; set; } = "Indexed";
    public string? ExtractedText { get; set; }
    public string? OcrText { get; set; }
    public List<string>? Tags { get; set; }
    public string? Category { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
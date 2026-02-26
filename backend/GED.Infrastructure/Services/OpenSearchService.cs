using OpenSearch.Client;
using OpenSearch.Net;
using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
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
                
                // Apply NLP-extracted filters
                if (nlQuery.ExtractedFilters != null && nlQuery.ExtractedFilters.Any())
                {
                    _logger.LogInformation("Applying {Count} NLP-extracted filters", nlQuery.ExtractedFilters.Count);
                    
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
                
                // Detect generic queries
                var lowerQuery = request.Query.ToLower().Trim();
                var genericPhrases = new[] { "all documents", "show all", "list all", "get all", "find all" };
                
                if (genericPhrases.Any(phrase => lowerQuery.Contains(phrase)) || 
                    (!nlQuery.Keywords.Any() && lowerQuery.Split(' ').All(w => w.Length <= 4)))
                {
                    processedQuery = string.Empty;
                    _logger.LogInformation("Detected generic 'show all' query - returning all documents");
                }
            }

            // Build OpenSearch query
            var searchResponse = await _client.SearchAsync<DocumentIndexModel>(s => s
                .Index(DocumentIndex)
                .From((request.Page - 1) * request.PageSize)
                .Size(request.PageSize)
                .Query(q => BuildPrecisionQuery(q, processedQuery, request, nlQuery))
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

            // Normalize scores to 0-1.0 range
            if (documents.Any())
            {
                var maxScore = documents.Max(d => d.Score);
                if (maxScore > 0)
                {
                    foreach (var doc in documents)
                    {
                        doc.Score = doc.Score / maxScore;
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


private QueryContainer BuildPrecisionQuery(
    QueryContainerDescriptor<DocumentIndexModel> q,
    string query,
    CoreSearchRequest request,
    NaturalLanguageQuery? nlQuery)
{
    var mustQueries = new List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>>();
    var shouldQueries = new List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>>();
    var filterQueries = new List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>>();

    // ========== HARD FILTERS (Must match, no scoring) ==========
    
    // Status filter - only indexed documents
    filterQueries.Add(sq => sq.Term(t => t.Field("status").Value("Indexed")));
    
    // Content type filter
    if (request.ContentTypes?.Any() == true)
    {
        _logger.LogInformation("🔒 Applying HARD contentType filter: {Types}", 
            string.Join(", ", request.ContentTypes));
        
        filterQueries.Add(ctq => ctq.Terms(t => t
            .Field(doc => doc.ContentType)
            .Terms(request.ContentTypes)
        ));
    }

    // Category filter (from manual filters OR NLP)
    var categoriesToFilter = new List<string>();
    
    if (request.Categories?.Any() == true)
    {
        categoriesToFilter.AddRange(request.Categories);
    }
    
    if (nlQuery?.Entities != null)
    {
        var categoryEntities = nlQuery.Entities
            .Where(e => e.StartsWith("CATEGORY:"))
            .Select(e => e.Substring(9))
            .Select(c => char.ToUpper(c[0]) + c.Substring(1))
            .ToList();
        
        if (categoryEntities.Any())
        {
            categoriesToFilter.AddRange(categoryEntities);
            _logger.LogInformation("✅ Added category from NLP: {Categories}", 
                string.Join(", ", categoryEntities));
        }
    }
    
    if (categoriesToFilter.Any())
    {
        _logger.LogInformation("🔒 Applying category filter: {Categories}", 
            string.Join(", ", categoriesToFilter));
        
        filterQueries.Add(cq => cq.Bool(b => b
            .Should(categoriesToFilter.Select(cat => 
                new Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>(
                    sq => sq.Term(t => t
                        .Field("category.keyword")
                        .Value(cat)
                        .CaseInsensitive(true)
                    )
                )
            ).ToArray())
            .MinimumShouldMatch(1)
        ));
    }

    // Tags filter
    if (request.Tags?.Any() == true)
    {
        filterQueries.Add(tq => tq.Terms(t => t
            .Field(doc => doc.Tags)
            .Terms(request.Tags)
        ));
    }

    // Date range filter
    if (request.FromDate.HasValue || request.ToDate.HasValue)
    {
        _logger.LogInformation("🔒 Applying date range filter: {From} to {To}", 
            request.FromDate?.ToString("yyyy-MM-dd") ?? "start", 
            request.ToDate?.ToString("yyyy-MM-dd") ?? "end");
        
        filterQueries.Add(dq => dq.Bool(b => b
            .Should(
                s => s.Bool(b1 => b1
                    .Must(
                        m => m.Exists(e => e.Field(doc => doc.DocumentDate)),
                        m => m.DateRange(dr => dr
                            .Field(doc => doc.DocumentDate)
                            .GreaterThanOrEquals(request.FromDate)
                            .LessThanOrEquals(request.ToDate)
                        )
                    )
                ),
                s => s.Bool(b2 => b2
                    .Must(
                        m => m.Bool(nb => nb
                            .MustNot(mn => mn.Exists(e => e.Field(doc => doc.DocumentDate)))
                        ),
                        m => m.DateRange(dr => dr
                            .Field(doc => doc.CreatedAt)
                            .GreaterThanOrEquals(request.FromDate)
                            .LessThanOrEquals(request.ToDate)
                        )
                    )
                )
            )
            .MinimumShouldMatch(1)
        ));
    }

    // ========== TEXT SEARCH QUERIES (Tiered scoring) ==========
    
    if (!string.IsNullOrWhiteSpace(query))
    {
        var cleanQuery = query.Trim();
        var normalizedQuery = NormalizeSearchQuery(cleanQuery);
        var queryVariations = GenerateQueryVariations(cleanQuery);
        
        _logger.LogInformation("🔍 Search query: '{Original}' → normalized: '{Normalized}', variations: [{Variations}]", 
            cleanQuery, normalizedQuery, string.Join(", ", queryVariations));

        var isMultiWord = cleanQuery.Contains(' ');

        if (isMultiWord)
        {
            shouldQueries.Add(sq => sq.MatchPhrase(mp => mp
                .Field(doc => doc.Title)
                .Query(cleanQuery)
                .Boost(100.0)
            ));
        }
        
        foreach (var variant in queryVariations)
        {
            shouldQueries.Add(sq => sq.Term(t => t
                .Field("category.keyword")
                .Value(variant)
                .CaseInsensitive(true)
                .Boost(80.0)
            ));
        }
        
        foreach (var variant in queryVariations)
        {
            shouldQueries.Add(sq => sq.Term(t => t
                .Field("title.keyword")
                .Value(variant)
                .CaseInsensitive(true)
                .Boost(70.0)
            ));
        }

        foreach (var variant in queryVariations)
        {
            shouldQueries.Add(sq => sq.Prefix(p => p
                .Field(doc => doc.Title)
                .Value(variant.ToLower())
                .CaseInsensitive(true)
                .Boost(50.0)
            ));
        }
        
        foreach (var variant in queryVariations)
        {
            shouldQueries.Add(sq => sq.MultiMatch(m => m
                .Query(variant)
                .Fields(f => f
                    .Field(doc => doc.Title, 20.0)
                    .Field(doc => doc.Category, 15.0)
                    .Field(doc => doc.FileName, 10.0)
                    .Field(doc => doc.Description, 5.0)
                )
                .Type(TextQueryType.BestFields)
                .Operator(Operator.And)
                .Boost(40.0)
            ));
        }
        
        shouldQueries.Add(sq => sq.MatchPhrasePrefix(mpp => mpp
            .Field(doc => doc.Title)
            .Query(cleanQuery)
            .MaxExpansions(20)
            .Boost(35.0)
        ));

        foreach (var variant in queryVariations)
        {
            shouldQueries.Add(sq => sq.MultiMatch(m => m
                .Query(variant)
                .Fields(f => f
                    .Field(doc => doc.Title, 10.0)
                    .Field(doc => doc.Category, 8.0)
                    .Field(doc => doc.FileName, 5.0)
                    .Field(doc => doc.Description, 3.0)
                    .Field(doc => doc.ExtractedText, 2.0)
                )
                .Type(TextQueryType.MostFields)
                .Operator(Operator.Or)
                .MinimumShouldMatch("50%")
                .Boost(25.0)
            ));
        }
        
        foreach (var variant in queryVariations)
        {
            shouldQueries.Add(sq => sq.Wildcard(w => w
                .Field(doc => doc.Title)
                .Value($"*{variant.ToLower()}*")
                .CaseInsensitive(true)
                .Boost(20.0)
            ));
            
            shouldQueries.Add(sq => sq.Wildcard(w => w
                .Field(doc => doc.FileName)
                .Value($"*{variant.ToLower()}*")
                .CaseInsensitive(true)
                .Boost(18.0)
            ));
        }

        foreach (var variant in queryVariations)
        {
            shouldQueries.Add(sq => sq.MultiMatch(m => m
                .Query(variant)
                .Fields(f => f
                    .Field(doc => doc.Title, 5.0)
                    .Field(doc => doc.Category, 4.0)
                    .Field(doc => doc.FileName, 3.0)
                )
                .Fuzziness(Fuzziness.Auto)
                .Operator(Operator.Or)
                .Boost(15.0)
            ));
        }

        shouldQueries.Add(sq => sq.Match(m => m
            .Field(doc => doc.ExtractedText)
            .Query(normalizedQuery)
            .Operator(Operator.Or)
            .MinimumShouldMatch("40%")
            .Boost(10.0)
        ));
        
        shouldQueries.Add(sq => sq.Match(m => m
            .Field(doc => doc.OcrText)
            .Query(normalizedQuery)
            .Operator(Operator.Or)
            .MinimumShouldMatch("40%")
            .Boost(5.0)
        ));

        if (nlQuery?.Keywords != null && nlQuery.Keywords.Any())
        {
            foreach (var keyword in nlQuery.Keywords)
            {
                var keywordVariations = GenerateQueryVariations(keyword);
                
                foreach (var variant in keywordVariations)
                {
                    shouldQueries.Add(sq => sq.Term(t => t
                        .Field("category.keyword")
                        .Value(variant)
                        .CaseInsensitive(true)
                        .Boost(30.0)
                    ));
                    
                    shouldQueries.Add(sq => sq.MultiMatch(m => m
                        .Query(variant)
                        .Fields(f => f
                            .Field(doc => doc.Title, 8.0)
                            .Field(doc => doc.Category, 6.0)
                            .Field(doc => doc.Description, 3.0)
                        )
                        .Operator(Operator.Or)
                        .Boost(12.0)
                    ));
                }
            }
        }
    }

    var minimumShouldMatch = CalculateMinimumShouldMatch(query, shouldQueries.Count);
    
    _logger.LogInformation("📊 Query structure: {ShouldQueries} scoring queries, {FilterQueries} filters, MinShouldMatch: {MinMatch}",
        shouldQueries.Count, filterQueries.Count, minimumShouldMatch);
    
    return q.Bool(b =>
    {
        var boolQuery = b;
        
        if (filterQueries.Any())
        {
            boolQuery = boolQuery.Filter(filterQueries.ToArray());
        }
        
        if (shouldQueries.Any())
        {
            boolQuery = boolQuery.Should(shouldQueries.ToArray());
            boolQuery = boolQuery.MinimumShouldMatch(minimumShouldMatch);
        }
        else
        {
            boolQuery = boolQuery.Must(m => m.MatchAll());
        }
        
        return boolQuery;
    });
}

private string NormalizeSearchQuery(string query)
{
    if (string.IsNullOrWhiteSpace(query))
        return query;
    
    var normalized = query.ToLower().Trim();
    
    if (normalized.EndsWith("s") && normalized.Length > 2 && !normalized.EndsWith("ss"))
    {
        normalized = normalized.TrimEnd('s');
    }
    
    if (normalized.EndsWith("ies") && normalized.Length > 4)
    {
        normalized = normalized.Substring(0, normalized.Length - 3) + "y";
    }
    
    return normalized;
}

private List<string> GenerateQueryVariations(string query)
{
    var variations = new List<string>();
    
    if (string.IsNullOrWhiteSpace(query))
        return variations;
    
    var cleaned = query.Trim();
    variations.Add(cleaned);
    
    var normalized = NormalizeSearchQuery(cleaned);
    if (normalized != cleaned)
    {
        variations.Add(normalized);
    }
    
    if (!cleaned.EndsWith("s", StringComparison.OrdinalIgnoreCase))
    {
        variations.Add(cleaned + "s");
    }
    
    var lower = cleaned.ToLower();
    if (!variations.Contains(lower))
    {
        variations.Add(lower);
    }
    
    if (lower.Length > 0)
    {
        var capitalized = char.ToUpper(lower[0]) + lower.Substring(1);
        if (!variations.Contains(capitalized))
        {
            variations.Add(capitalized);
        }
    }
    
    return variations.Distinct().ToList();
}

private string CalculateMinimumShouldMatch(string query, int totalShouldClauses)
{
    if (string.IsNullOrWhiteSpace(query))
    {
        return "0";
    }

    if (totalShouldClauses <= 10)
    {
        return "1";
    }
    else if (totalShouldClauses <= 30)
    {
        return "2";
    }
    else
    {
        return "3";
    }
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
        DocumentDate = doc.DocumentDate,
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

    /// <summary>
    /// FIX: Flattens JsonElement values in the Metadata dictionary so OpenSearch
    /// receives plain primitives (string, double, bool) instead of serialized
    /// JsonElement objects like {valueKind=3}.
    ///
    /// Root cause: when the upload controller deserializes JSON from the HTTP
    /// request body using System.Text.Json, dictionary values typed as `object`
    /// become JsonElement instances rather than native .NET primitives.  Those
    /// JsonElement objects then serialize to {valueKind=N} instead of the actual
    /// value, causing OpenSearch mapper_parsing_exception on text fields.
    /// </summary>
    private static Dictionary<string, object>? SanitizeMetadata(Dictionary<string, object>? metadata)
    {
        if (metadata == null) return null;

        var result = new Dictionary<string, object>(metadata.Count);
        foreach (var (key, value) in metadata)
        {
            result[key] = FlattenValue(value);
        }
        return result;
    }

    private static object FlattenValue(object? value)
    {
        if (value is null) return string.Empty;

        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String  => je.GetString() ?? string.Empty,
                JsonValueKind.Number  => je.TryGetInt64(out var l) ? (object)l : je.GetDouble(),
                JsonValueKind.True    => true,
                JsonValueKind.False   => false,
                JsonValueKind.Null    => string.Empty,
                // For arrays/objects fall back to raw JSON string so the field
                // at least has a safe text value instead of an unparseable object.
                _                     => je.GetRawText()
            };
        }

        return value;
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
        DocumentDate = document.DocumentDate,
        ModifiedAt = document.ModifiedAt,
        Status = document.Status.ToString(),
        ExtractedText = document.ExtractedText,
        OcrText = document.OcrText,
        Tags = document.Tags,
        Category = document.Category,
        // FIX: sanitize metadata to unwrap JsonElement values before sending
        // to OpenSearch — prevents mapper_parsing_exception on text fields
        // (e.g. metadata.category arriving as {valueKind=3} instead of "Contract")
        Metadata = SanitizeMetadata(document.Metadata)
    };
}

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

public class DocumentIndexModel
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
    public string Status { get; set; } = "Indexed";
    public string? ExtractedText { get; set; }
    public string? OcrText { get; set; }
    public List<string>? Tags { get; set; }
    public string? Category { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
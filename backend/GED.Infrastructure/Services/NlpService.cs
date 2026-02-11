using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

public class NlpService : INlpService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NlpService> _logger;
    private readonly bool _nlpEnabled;
    private readonly string? _llmEndpoint;
    private readonly string? _apiKey;

    public NlpService(
        HttpClient httpClient,
        ILogger<NlpService> logger,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _nlpEnabled = configuration.GetValue<bool>("NLP:Enabled");
        _llmEndpoint = configuration["NLP:LlmApiEndpoint"];
        _apiKey = configuration["NLP:ApiKey"];
    }

    public async Task<NaturalLanguageQuery> UnderstandQueryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (!_nlpEnabled || string.IsNullOrWhiteSpace(query))
        {
            return CreateBasicQuery(query);
        }

        try
        {
            // Extract keywords using basic NLP
            var keywords = await ExtractKeywordsAsync(query, 5, cancellationToken);

            // Extract entities (dates, file types, etc.)
            var entities = await ExtractEntitiesAsync(query, cancellationToken);

            // Determine intent
            var intent = DetermineIntent(query);

            // Extract filters from query - NOW WITH ACTUAL DATE CALCULATION
            var filters = ExtractFilters(query);

            return new NaturalLanguageQuery
            {
                OriginalQuery = query,
                ProcessedQuery = ProcessQuery(query, keywords),
                Keywords = keywords,
                Entities = entities,
                Intent = intent,
                ExtractedFilters = filters
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in NLP processing, falling back to basic query");
            return CreateBasicQuery(query);
        }
    }

    public async Task<List<string>> ExtractKeywordsAsync(
        string text,
        int maxKeywords = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        try
        {
            // Basic keyword extraction - split and filter
            var words = text.ToLower()
                .Split(new[] { ' ', ',', '.', '!', '?', ';', ':', '\n', '\r', '\t' },
                       StringSplitOptions.RemoveEmptyEntries);

            // Filter out common stop words
            var stopWords = new HashSet<string>
            {
                "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
                "of", "with", "by", "from", "as", "is", "was", "are", "were", "be",
                "been", "being", "have", "has", "had", "do", "does", "did", "will",
                "would", "should", "could", "may", "might", "can", "must", "this",
                "that", "these", "those", "i", "you", "he", "she", "it", "we", "they"
            };

            var keywords = words
                .Where(w => w.Length > 2 && !stopWords.Contains(w))
                .GroupBy(w => w)
                .OrderByDescending(g => g.Count())
                .Take(maxKeywords)
                .Select(g => g.Key)
                .ToList();

            return keywords;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting keywords");
            return new List<string>();
        }
    }

    public async Task<List<string>> ExtractEntitiesAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var entities = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return entities;
        }

        try
        {
            var lowerText = text.ToLower();

            // Extract date entities
            var datePatterns = new[]
            {
                @"\d{4}", // year
                @"january|february|march|april|may|june|july|august|september|october|november|december",
                @"last month|this month|next month|last year|this year|next year",
                @"today|yesterday|tomorrow"
            };

            foreach (var pattern in datePatterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(lowerText, pattern);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    entities.Add($"DATE:{match.Value}");
                }
            }

            // Extract file type entities
            var fileTypes = new[] { "pdf", "doc", "docx", "xls", "xlsx", "jpg", "jpeg", "png", "tiff" };
            foreach (var fileType in fileTypes)
            {
                if (lowerText.Contains(fileType))
                {
                    entities.Add($"FILETYPE:{fileType}");
                }
            }

            // Extract document categories
            var categories = new[] { "invoice", "contract", "report", "letter", "memo", "presentation" };
            foreach (var category in categories)
            {
                if (lowerText.Contains(category))
                {
                    entities.Add($"CATEGORY:{category}");
                }
            }

            return entities.Distinct().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting entities");
            return entities;
        }
    }

    public async Task<float> CalculateSimilarityAsync(
        string text1,
        string text2,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
        {
            return 0f;
        }

        try
        {
            // Simple Jaccard similarity based on words
            var words1 = text1.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var words2 = text2.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            var intersection = words1.Intersect(words2).Count();
            var union = words1.Union(words2).Count();

            return union > 0 ? (float)intersection / union : 0f;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating similarity");
            return 0f;
        }
    }

    public async Task<string> SummarizeTextAsync(
        string text,
        int maxLength = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        try
        {
            if (text.Length <= maxLength)
            {
                return text;
            }

            // Simple extractive summarization - take first sentences up to maxLength
            var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            var summary = new StringBuilder();

            foreach (var sentence in sentences)
            {
                var trimmed = sentence.Trim();
                if (summary.Length + trimmed.Length + 1 <= maxLength)
                {
                    if (summary.Length > 0) summary.Append(". ");
                    summary.Append(trimmed);
                }
                else
                {
                    break;
                }
            }

            return summary.ToString() + (summary.Length < text.Length ? "..." : "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error summarizing text");
            return text.Substring(0, Math.Min(maxLength, text.Length)) + "...";
        }
    }

    private NaturalLanguageQuery CreateBasicQuery(string query)
    {
        return new NaturalLanguageQuery
        {
            OriginalQuery = query ?? string.Empty,
            ProcessedQuery = query ?? string.Empty,
            Keywords = new List<string>(),
            Entities = new List<string>(),
            Intent = QueryIntent.Search,
            ExtractedFilters = new Dictionary<string, string>()
        };
    }

    private string ProcessQuery(string query, List<string> keywords)
    {
        // Remove common question words and enhance with keywords
        var processedQuery = query;
        var questionWords = new[] { "show me", "find me", "get me", "search for", "look for", "give me" };

        foreach (var word in questionWords)
        {
            processedQuery = processedQuery.Replace(word, "", StringComparison.OrdinalIgnoreCase);
        }

        return processedQuery.Trim();
    }

    private QueryIntent DetermineIntent(string query)
    {
        var lowerQuery = query.ToLower();

        if (lowerQuery.Contains("find") || lowerQuery.Contains("search") || lowerQuery.Contains("show"))
        {
            return QueryIntent.Find;
        }
        else if (lowerQuery.Contains("list") || lowerQuery.Contains("all"))
        {
            return QueryIntent.List;
        }
        else if (lowerQuery.Contains("filter") || lowerQuery.Contains("where") || lowerQuery.Contains("only"))
        {
            return QueryIntent.Filter;
        }
        else if (lowerQuery.Contains("compare") || lowerQuery.Contains("difference") || lowerQuery.Contains("vs"))
        {
            return QueryIntent.Compare;
        }

        return QueryIntent.Search;
    }

private Dictionary<string, string> ExtractFilters(string query)
{
    var filters = new Dictionary<string, string>();
    var lowerQuery = query.ToLower();
    var now = DateTime.UtcNow;

    _logger.LogInformation("Extracting filters from query: '{Query}' (current date: {Now})", query, now);

    // Extract year filter
    var yearMatch = System.Text.RegularExpressions.Regex.Match(query, @"\b(20\d{2})\b");
    if (yearMatch.Success)
    {
        var year = int.Parse(yearMatch.Value);
        filters["fromDate"] = new DateTime(year, 1, 1).ToString("o");
        filters["toDate"] = new DateTime(year, 12, 31, 23, 59, 59).ToString("o");
        _logger.LogInformation("Parsed year '{Year}' as {From} to {To}", year, filters["fromDate"], filters["toDate"]);
    }

    // ⭐ FIXED: Extract file type filter using WORD BOUNDARIES
    var fileTypes = new[] { "pdf", "doc", "docx", "xls", "xlsx", "jpg", "jpeg", "png" };
    foreach (var fileType in fileTypes)
    {
        // Use word boundary regex to match whole words only
        // \b ensures we match "pdf" or "pdfs" but NOT "pdf" within "documents"
        var pattern = $@"\b{fileType}s?\b";  // 's?' makes the plural optional
        if (System.Text.RegularExpressions.Regex.IsMatch(lowerQuery, pattern))
        {
            // Store the singular form (remove trailing 's' if present)
            var singularType = fileType.EndsWith("s") ? fileType : fileType.TrimEnd('s');
            filters["filetype"] = singularType;
            _logger.LogInformation("Detected file type: {FileType}", singularType);
            break;
        }
    }

    // Extract date range filters - CALCULATE ACTUAL DATES
    if (lowerQuery.Contains("last month"))
    {
        var firstDayLastMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
        var lastDayLastMonth = firstDayLastMonth.AddMonths(1).AddDays(-1);
        filters["fromDate"] = firstDayLastMonth.ToString("o");
        filters["toDate"] = lastDayLastMonth.Date.AddDays(1).AddSeconds(-1).ToString("o");
        _logger.LogInformation("Parsed 'last month' as {From} to {To}", filters["fromDate"], filters["toDate"]);
    }
    else if (lowerQuery.Contains("last year"))
    {
        filters["fromDate"] = new DateTime(now.Year - 1, 1, 1).ToString("o");
        filters["toDate"] = new DateTime(now.Year - 1, 12, 31, 23, 59, 59).ToString("o");
        _logger.LogInformation("Parsed 'last year' as {From} to {To}", filters["fromDate"], filters["toDate"]);
    }
    else if (lowerQuery.Contains("this month"))
    {
        var firstDayThisMonth = new DateTime(now.Year, now.Month, 1);
        filters["fromDate"] = firstDayThisMonth.ToString("o");
        filters["toDate"] = now.ToString("o");
        _logger.LogInformation("Parsed 'this month' as {From} to {To}", filters["fromDate"], filters["toDate"]);
    }
    else if (lowerQuery.Contains("this year"))
    {
        filters["fromDate"] = new DateTime(now.Year, 1, 1).ToString("o");
        filters["toDate"] = now.ToString("o");
        _logger.LogInformation("Parsed 'this year' as {From} to {To}", filters["fromDate"], filters["toDate"]);
    }
    else if (lowerQuery.Contains("last week"))
    {
        var lastWeekStart = now.AddDays(-7).Date;
        filters["fromDate"] = lastWeekStart.ToString("o");
        filters["toDate"] = now.ToString("o");
        _logger.LogInformation("Parsed 'last week' as {From} to {To}", filters["fromDate"], filters["toDate"]);
    }
    else if (lowerQuery.Contains("today"))
    {
        filters["fromDate"] = now.Date.ToString("o");
        filters["toDate"] = now.ToString("o");
        _logger.LogInformation("Parsed 'today' as {From} to {To}", filters["fromDate"], filters["toDate"]);
    }
    else if (lowerQuery.Contains("yesterday"))
    {
        var yesterday = now.AddDays(-1).Date;
        filters["fromDate"] = yesterday.ToString("o");
        filters["toDate"] = yesterday.AddDays(1).AddSeconds(-1).ToString("o");
        _logger.LogInformation("Parsed 'yesterday' as {From} to {To}", filters["fromDate"], filters["toDate"]);
    }

    // Try to extract month names
    var months = new Dictionary<string, int>
    {
        {"january", 1}, {"february", 2}, {"march", 3}, {"april", 4},
        {"may", 5}, {"june", 6}, {"july", 7}, {"august", 8},
        {"september", 9}, {"october", 10}, {"november", 11}, {"december", 12}
    };

    foreach (var month in months)
    {
        if (lowerQuery.Contains(month.Key))
        {
            // Determine year - if month hasn't occurred this year yet, assume last year
            var targetYear = now.Year;
            if (month.Value > now.Month)
            {
                targetYear = now.Year - 1;
            }
            
            var firstDay = new DateTime(targetYear, month.Value, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);
            
            filters["fromDate"] = firstDay.ToString("o");
            filters["toDate"] = lastDay.Date.AddDays(1).AddSeconds(-1).ToString("o");
            _logger.LogInformation("Parsed month '{Month}' as {From} to {To}", 
                month.Key, filters["fromDate"], filters["toDate"]);
            break;
        }
    }

    _logger.LogInformation("Extracted {Count} filters from query", filters.Count);
    return filters;
}

}
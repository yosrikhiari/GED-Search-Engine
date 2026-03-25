using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace GED.Infrastructure.Services;

/// <summary>
/// Rule-based query classifier supporting English, French, and Arabic.
/// Uses keyword matching and pattern detection to classify queries
/// into intent types for optimized RAG retrieval.
/// </summary>
public class QueryClassifierService : IQueryClassifierService
{
    private readonly ILogger<QueryClassifierService> _logger;
    private readonly bool _enabled;
    private readonly float _lowConfidenceThreshold;
    private readonly bool _fallbackToFactual;
    private readonly int _defaultTopK;
    private readonly float _defaultConfidenceThreshold;
    private readonly int _comparisonMaxChunksPerDoc;

    // Query type configurations
    private readonly Dictionary<string, (int TopK, float Threshold)> _typeConfigs;

    // Stop words for keyword extraction
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","and","or","but","in","on","at","to","for","of","with","by","from",
        "as","is","was","are","were","be","been","being","have","has","had","do","does",
        "did","will","would","should","could","may","might","can","must","this","that",
        "these","those","i","you","he","she","it","we","they","me","my","your","his","her",
        "its","our","their","what","which","who","whom","where","when","why","how",
        "le","la","les","un","une","des","du","de","et","ou","mais","dans","sur","avec",
        "pour","par","en","ce","se","sa","son","leur","nos","vos","je","tu","il","elle",
        "nous","vous","ils","elles","est","sont","montre","cherche","trouve","affiche",
        "un","une","des"
    };

    // Strong patterns (add 0.2 to confidence)
    private static readonly Dictionary<string, List<string>> StrongPatterns = new()
    {
        ["factual"] = new()
        {
            // English
            @"^what\s+is\s+the", @"^what\s+is\s+", @"^who\s+(is|are|was|were)",
            @"^where\s+is", @"^how\s+much\s+", @"^how\s+many\s+",
            @"what\s+(is|are)\s+the\s+\w+\s+(number|date|name)",
            // French
            @"^quel\s+(est|sont)", @"^quelle\s+(est|sont)", @"^qui\s+(est|sont)",
            @"^où\s+est", @"^comment\s+", @"^combien\s+(de|est-ce)",
            @"numéro\s+de\s+", @"date\s+de\s+",
            // Arabic
            @"^ما\s+هو", @"^ما\s+هي", @"^من\s+(هو|هي)",
            @"^أين\s+", @"^كم\s+(يبلغ|عدد)"
        },
        ["summarization"] = new()
        {
            // English
            @"summarize", @"summary", @"overview", @"gist", @"tl;dr",
            @"give\s+me\s+(a\s+)?", @"what\s+does\s+it\s+say",
            // French
            @"résumer", @"résumé\s+", @"aperçu", @"donne.*vue\s+d'ensemble",
            @"en\s+bref", @"fond",
            // Arabic
            @"ملخص", @"موجز", @"نظرة\s+عامة", @"بشكل\s+عام"
        },
        ["comparison"] = new()
        {
            // English
            @"compare", @"vs\.?", @"versus", @"difference\s+between",
            @"different\s+from", @"what('s|\s+is)\s+the\s+difference",
            // French
            @"comparer", @"différence", @"vs\.?", @"versus",
            @"entre\s+.*et\s+", @"différent\s+de",
            // Arabic
            @"مقارنة", @"الفرق\s+بين", @"مختلف\s+عن", @"بين\s+"
        },
        ["extraction"] = new()
        {
            // English
            @"when\s+did", @"when\s+was", @"when\s+is", @"date\s+of",
            @"how\s+much\s+is", @"how\s+much\s+was", @"amount\s+of",
            @"list\s+all", @"extract\s+all", @"find\s+all", @"give\s+me\s+all",
            @"names?\s+of", @"numbers?\s+of",
            // French
            @"quand\s+", @"date\s+", @"montant\s+", @"somme\s+",
            @"liste\s+de\s+tous", @"extraire\s+tous", @"trouve.*tous",
            @"noms?\s+de", @"numéros?\s+de",
            // Arabic
            @"متى", @"تاريخ", @"مبلغ", @"مبلغ\s+",
            @"جميع", @"اسم", @"أسماء", @"ارقام"
        }
    };

    // Weak patterns (no bonus)
    private static readonly Dictionary<string, List<string>> WeakPatterns = new()
    {
        ["factual"] = new()
        {
            @"show\s+me", @"find\s+", @"get\s+", @"look\s+for",
            @"trouve", @"cherche", @"affiche", @"montre", @"أظهر", @"أعطني"
        },
        ["summarization"] = new()
        {
            @"tell\s+me\s+about", @"explain", @"describe",
            @"parler\s+de", @"expliquer", @"décrire", @"يتحدث", @"يشرح"
        }
    };

    public bool IsEnabled => _enabled;

    public QueryClassifierService(
        IConfiguration configuration,
        ILogger<QueryClassifierService> logger)
    {
        _logger = logger;
        _enabled = configuration.GetValue<bool>("RAG:EnableQueryClassification", true);
        _lowConfidenceThreshold = configuration.GetValue<float>("RAG:QueryClassification:LowConfidenceThreshold", 0.6f);
        _fallbackToFactual = configuration.GetValue<bool>("RAG:QueryClassification:FallbackToFactual", true);
        _defaultTopK = configuration.GetValue<int>("RAG:TopK", 5);
        _defaultConfidenceThreshold = configuration.GetValue<float>("RAG:ConfidenceThreshold", 0.45f);
        _comparisonMaxChunksPerDoc = configuration.GetValue<int>("RAG:QueryClassification:ComparisonMaxChunksPerDoc", 2);

        // Load type-specific configs - use explicit defaults if not in config
        _typeConfigs = new Dictionary<string, (int TopK, float Threshold)>
        {
            ["factual"] = (
                configuration.GetValue<int?>("RAG:QueryClassification:TypeConfigs:factual:topK") ?? 5,
                configuration.GetValue<float?>("RAG:QueryClassification:TypeConfigs:factual:confidenceThreshold") ?? 0.5f
            ),
            ["summarization"] = (
                configuration.GetValue<int?>("RAG:QueryClassification:TypeConfigs:summarization:topK") ?? 10,
                configuration.GetValue<float?>("RAG:QueryClassification:TypeConfigs:summarization:confidenceThreshold") ?? 0.3f
            ),
            ["comparison"] = (
                configuration.GetValue<int?>("RAG:QueryClassification:TypeConfigs:comparison:topK") ?? 8,
                configuration.GetValue<float?>("RAG:QueryClassification:TypeConfigs:comparison:confidenceThreshold") ?? 0.35f
            ),
            ["extraction"] = (
                configuration.GetValue<int?>("RAG:QueryClassification:TypeConfigs:extraction:topK") ?? 5,
                configuration.GetValue<float?>("RAG:QueryClassification:TypeConfigs:extraction:confidenceThreshold") ?? 0.4f
            )
        };

        _logger.LogInformation(
            "Query classifier enabled: {Enabled}, fallback: {Fallback}, low-conf: {LowConf}, comparison chunks/doc: {MaxPerDoc}",
            _enabled, _fallbackToFactual, _lowConfidenceThreshold, _comparisonMaxChunksPerDoc);
    }

    /// <inheritdoc />
    public Task<QueryClassificationResult> ClassifyAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(new QueryClassificationResult
            {
                QueryType = "factual",
                Confidence = 1.0f,
                RecommendedTopK = _defaultTopK,
                RecommendedConfidenceThreshold = _defaultConfidenceThreshold
            });
        }

        // Normalize query (lowercase, remove diacritics for Arabic)
        var normalizedQuery = NormalizeQuery(query);

        // Extract keywords
        var keywords = ExtractKeywords(normalizedQuery);
        var matchedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check strong patterns
        var strongMatches = CheckPatterns(normalizedQuery, StrongPatterns, matchedKeywords);
        var weakMatches = CheckPatterns(normalizedQuery, WeakPatterns, matchedKeywords);

        _logger.LogDebug("Query '{Query}': strongMatches={Strong}, weakMatches={Weak}, keywords={Keywords}",
            query, string.Join(",", strongMatches), string.Join(",", weakMatches), string.Join(",", matchedKeywords));

        // Determine primary type from strong matches - must have at least one match
        var primaryType = strongMatches.Any() 
            ? strongMatches.OrderByDescending(kvp => kvp.Value).First()
            : default(KeyValuePair<string, int>);

        // Calculate confidence
        float keywordScore = 0f;
        if (keywords.Count > 0)
        {
            keywordScore = (float)matchedKeywords.Count / keywords.Count;
        }

        // Pattern bonus: 0.35 per strong match (more generous), max 0.7
        float patternBonus = Math.Min(strongMatches.Count * 0.35f, 0.7f);

        // Combined confidence:
        // - If we have strong patterns: start at 0.4 + bonus (0.4 + 0.35 = 0.75 min)
        // - If only weak patterns: use keywordScore * 0.5 + weakBonus
        float confidence;
        if (strongMatches.Count > 0)
        {
            // Strong patterns should give high confidence
            confidence = Math.Min(0.4f + patternBonus, 1.0f);
        }
        else
        {
            // Weak patterns only: rely more on keywords
            float weakBonus = Math.Min(weakMatches.Count * 0.15f, 0.3f);
            confidence = Math.Min(keywordScore * 0.5f + weakBonus, 1.0f);
        }

        // Determine final type
        var queryType = primaryType.Key ?? "factual";

        // Build result
        var config = _typeConfigs.GetValueOrDefault(queryType, (_defaultTopK, _defaultConfidenceThreshold));
        var result = new QueryClassificationResult
        {
            QueryType = queryType,
            Confidence = confidence,
            RecommendedTopK = config.Item1,
            RecommendedConfidenceThreshold = config.Item2,
            IsAmbiguous = confidence < _lowConfidenceThreshold,
            MatchedKeywords = matchedKeywords.ToList(),
            MatchedPatterns = strongMatches.Keys.Concat(weakMatches.Keys).ToList()
        };

        // Apply fallback if needed
        if (_fallbackToFactual && (result.IsAmbiguous || confidence < _lowConfidenceThreshold))
        {
            var originalType = result.QueryType;
            result.QueryType = "factual";
            result.Confidence = 0.5f;
            result.RecommendedTopK = _defaultTopK;
            result.RecommendedConfidenceThreshold = _defaultConfidenceThreshold;
            result.IsAmbiguous = true;
            result.OriginalType = originalType;

            _logger.LogWarning(
                "⚠️ Query classification fallback: {OriginalType} → factual (confidence: {Conf}, keywords matched: {Keywords})",
                originalType, confidence, string.Join(", ", matchedKeywords));
        }

        _logger.LogDebug(
            "Query classified: type={Type}, confidence={Conf:F2}, keywords={Keywords}, patterns={Patterns}",
            result.QueryType, result.Confidence,
            string.Join(", ", result.MatchedKeywords),
            string.Join(", ", result.MatchedPatterns));

        return Task.FromResult(result);
    }

    /// <summary>
    /// Extracts meaningful keywords from query.
    /// </summary>
    private static List<string> ExtractKeywords(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<string>();

        var words = Regex.Split(query.ToLower(), @"[\s,.\-!?;:'""()\[\]{}]+")
            .Where(w => w.Length > 2 && !StopWords.Contains(w))
            .ToList();

        return words;
    }

    /// <summary>
    /// Checks query against patterns and returns matches.
    /// </summary>
    private static Dictionary<string, int> CheckPatterns(
        string query,
        Dictionary<string, List<string>> patternGroups,
        HashSet<string> matchedKeywords)
    {
        var matches = new Dictionary<string, int>();

        foreach (var (queryType, patterns) in patternGroups)
        {
            foreach (var pattern in patterns)
            {
                if (Regex.IsMatch(query, pattern, RegexOptions.IgnoreCase))
                {
                    matches[queryType] = matches.GetValueOrDefault(queryType, 0) + 1;

                    // Also mark keywords from pattern as matched
                    var patternWords = Regex.Matches(pattern, @"\w+")
                        .Select(m => m.Value.ToLower())
                        .Where(w => !StopWords.Contains(w));
                    foreach (var word in patternWords)
                    {
                        if (query.Contains(word, StringComparison.OrdinalIgnoreCase))
                            matchedKeywords.Add(word);
                    }
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// Normalizes query: lowercase and remove Arabic diacritics.
    /// </summary>
    private static string NormalizeQuery(string query)
    {
        var normalized = query.ToLower();

        // Remove Arabic diacritics (tashkeel)
        normalized = Regex.Replace(normalized, "[\u064B-\u065F\u0670]", "");

        return normalized;
    }

    /// <summary>
    /// Gets the configured max chunks per document for comparison queries.
    /// </summary>
    public int ComparisonMaxChunksPerDoc => _comparisonMaxChunksPerDoc;
}

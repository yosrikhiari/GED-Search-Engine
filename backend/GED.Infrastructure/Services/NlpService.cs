using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GED.Core.Interfaces;
using GED.Core.Models;

namespace GED.Infrastructure.Services;

/// <summary>
/// Multilingual NLP Service.
///
/// Design principles:
///   1. Language detection is PURELY LOCAL — character-range heuristics, zero latency.
///   2. Keyword / entity / filter extraction is PURELY LOCAL — rule-based, zero latency.
///   3. The ONLY Ollama call in the search path is GenerateEmbeddingAsync, which uses
///      nomic-embed-text (an embedding model, not a text-generation model).
///      Embeddings are ~50-100ms and language-agnostic by design.
///   4. Ollama text-generation (llama3.2) is NEVER called from the search path.
///      It is reserved exclusively for RAG answer generation (RagService).
///
/// This means a French query like "montrez-moi les factures de janvier" and its Arabic
/// equivalent "أرني فواتير يناير" produce similar embedding vectors and find the same
/// documents — without any translation step.
/// </summary>
public class NlpService : INlpService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NlpService> _logger;
    private readonly bool _nlpEnabled;
    private readonly string _embedEndpoint;   // e.g. http://localhost:11434/api/embed
    private readonly string _embedModel;      // nomic-embed-text

    // ── Stop-word sets (multilingual) ─────────────────────────────────────────
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // English
        "the","a","an","and","or","but","in","on","at","to","for","of","with","by","from",
        "as","is","was","are","were","be","been","being","have","has","had","do","does",
        "did","will","would","should","could","may","might","can","must","this","that",
        "these","those","i","you","he","she","it","we","they","show","me","find","get",
        "give","list","all","search","look","please","want","need",
        // French
        "le","la","les","un","une","des","du","de","d","l","et","ou","mais","donc","que",
        "qui","quoi","dont","tous","tout","toute","toutes","dans","sur","sous","avec","pour",
        "par","en","au","aux","ce","se","sa","son","leur","leurs","mes","tes","ses","nos",
        "vos","je","tu","il","elle","nous","vous","ils","elles","me","te","lui","est","sont",
        "montre","moi","cherche","trouve","affiche","donne","veux","besoin","voir",
        // Arabic
        "في","من","إلى","على","عن","مع","هذا","هذه","التي","الذي","كل","جميع","أو","و",
        "لا","ما","هل","عند","كان","كانت","هو","هي","هم","نحن","أنت","أنا","أرني","أعطني",
        "ابحث","اعرض","أظهر","جد","قدم"
    };

    public NlpService(
        HttpClient httpClient,
        ILogger<NlpService> logger,
        IConfiguration configuration)
    {
        _httpClient  = httpClient;
        _logger      = logger;
        _nlpEnabled  = configuration.GetValue<bool>("NLP:Enabled", true);
        // Embedding endpoint — separate from the generation endpoint
        _embedEndpoint = configuration["NLP:EmbedApiEndpoint"]
                         ?? "http://localhost:11434/api/embed";
        _embedModel  = configuration["NLP:EmbedModel"] ?? "nomic-embed-text";
    }

    // ── INlpService ───────────────────────────────────────────────────────────

    /// <summary>
    /// Fully local. Detects language, extracts keywords, entities, filters, and intent.
    /// No network calls. Completes in under 5ms.
    /// </summary>
    public Task<NaturalLanguageQuery> UnderstandQueryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(new NaturalLanguageQuery
            {
                OriginalQuery    = query ?? string.Empty,
                ProcessedQuery   = string.Empty,
                IsUnderstood     = false,
                DetectedLanguage = "unknown"
            });
        }

        var lang     = DetectLanguage(query);
        var keywords = ExtractKeywordsLocal(query, 8);
        var entities = ExtractEntitiesLocal(query);
        var intent   = DetermineIntent(query);
        var filters  = ExtractFilters(query);

        // A query is "not understood" when it yields zero keywords after stop-word
        // removal — i.e. it is pure stop words, single chars, or gibberish.
        var isUnderstood = keywords.Any() || entities.Any();

        var processed = ProcessQuery(query, keywords);

        return Task.FromResult(new NaturalLanguageQuery
        {
            OriginalQuery    = query,
            ProcessedQuery   = processed,
            Keywords         = keywords,
            Entities         = entities,
            Intent           = intent,
            ExtractedFilters = filters,
            DetectedLanguage = lang,
            IsUnderstood     = isUnderstood
        });
    }

    public Task<List<string>> ExtractKeywordsAsync(
        string text, int maxKeywords = 10,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ExtractKeywordsLocal(text, maxKeywords));

    public Task<List<string>> ExtractEntitiesAsync(
        string text,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ExtractEntitiesLocal(text));

    public Task<float> CalculateSimilarityAsync(
        string text1, string text2,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return Task.FromResult(0f);

        var w1 = text1.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var w2 = text2.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var intersection = w1.Intersect(w2).Count();
        var union        = w1.Union(w2).Count();
        return Task.FromResult(union > 0 ? (float)intersection / union : 0f);
    }

    public Task<string> SummarizeTextAsync(
        string text, int maxLength = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
            return Task.FromResult(text ?? string.Empty);

        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        var sb        = new StringBuilder();
        foreach (var s in sentences)
        {
            var t = s.Trim();
            if (sb.Length + t.Length + 2 <= maxLength)
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(t);
            }
            else break;
        }
        return Task.FromResult(sb.Length > 0 ? sb + "…" : text[..maxLength] + "…");
    }

    /// <summary>
    /// Calls Ollama's /api/embed with nomic-embed-text to produce a 768-dimensional
    /// vector for the given text. Returns null if Ollama is unavailable — the caller
    /// (OpenSearchService) degrades gracefully to BM25-only search.
    ///
    /// Why nomic-embed-text?
    ///   • Multilingual by training — Arabic, French, English all work natively.
    ///   • 768 dimensions — good balance of precision vs index size.
    ///   • Fast: ~50-150ms on CPU, ~10-30ms on GPU.
    ///   • Much faster than any text-generation model.
    /// </summary>
    public async Task<float[]?> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!_nlpEnabled || string.IsNullOrWhiteSpace(text))
            return null;

        // Truncate to ~4000 chars — nomic-embed-text context window is ~8192 tokens
        var input = text.Length > 4000 ? text[..4000] : text;

        try
        {
            var requestBody = new OllamaEmbedRequest
            {
                Model = _embedModel,
                Input = input
            };

            using var response = await _httpClient.PostAsJsonAsync(
                _embedEndpoint, requestBody, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Ollama embed returned {Status} — falling back to BM25-only",
                    response.StatusCode);
                return null;
            }

            var result = await response.Content
                .ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken: cancellationToken);

            var embedding = result?.Embeddings?.FirstOrDefault();
            if (embedding == null || embedding.Length == 0)
            {
                _logger.LogWarning("Ollama embed returned empty vector");
                return null;
            }

            return embedding;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Ollama is down or timed out — not a fatal error, BM25 will handle it
            _logger.LogWarning("Ollama embed unavailable ({Message}) — BM25-only mode", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error generating embedding");
            return null;
        }
    }

    // ── Language detection (pure local, zero latency) ─────────────────────────

    /// <summary>
    /// Detects language from character ranges — no network, no model, ~0.1ms.
    ///
    /// Strategy:
    ///   Arabic    → dominant Unicode block \u0600–\u06FF
    ///   French    → presence of accented chars OR common French tokens
    ///   English   → default (Latin script, no strong French signals)
    /// </summary>
    public static string DetectLanguage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "unknown";

        var arabicChars  = 0;
        var totalLetters = 0;

        foreach (var c in text)
        {
            if (char.IsLetter(c))
            {
                totalLetters++;
                if (c >= '\u0600' && c <= '\u06FF') arabicChars++;
            }
        }

        if (totalLetters == 0) return "unknown";

        // If >15% of letters are Arabic — it's Arabic
        if ((float)arabicChars / totalLetters > 0.15f) return "ar";

        // French signals: accented characters or French-specific tokens
        var frenchAccents = new[] { 'é', 'è', 'ê', 'ë', 'à', 'â', 'ù', 'û', 'ô', 'î', 'ï', 'ç', 'œ', 'æ' };
        var hasFrenchAccent = text.Any(c => frenchAccents.Contains(char.ToLower(c)));

        var lowerText = text.ToLower();
        var frenchTokens = new[] { "les", "des", "est", "dans", "avec", "pour", "sur", "qui", "que", "une", "par", "tout" };
        var hasFrenchToken = frenchTokens.Any(t => lowerText.Split(' ').Contains(t));

        if (hasFrenchAccent || hasFrenchToken) return "fr";

        return "en";
    }

    // ── Local keyword extraction ───────────────────────────────────────────────

    private static List<string> ExtractKeywordsLocal(string text, int maxKeywords)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();

        return text.ToLower()
            .Split(new[] { ' ', ',', '.', '!', '?', ';', ':', '\n', '\r', '\t', '\'', '"', '(', ')', '-' },
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !StopWords.Contains(w))
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count())
            .Take(maxKeywords)
            .Select(g => g.Key)
            .ToList();
    }

    // ── Local entity extraction ───────────────────────────────────────────────

    private static List<string> ExtractEntitiesLocal(string text)
    {
        var entities  = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return entities;

        var lower = text.ToLower();

        // ── File types ───────────────────────────────────────────────────────
        var fileTypes = new[] { "pdf", "doc", "docx", "xls", "xlsx", "jpg", "jpeg", "png", "txt" };
        foreach (var ft in fileTypes)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(lower, $@"\b{ft}s?\b"))
                entities.Add($"FILETYPE:{ft}");
        }

        // ── Document categories (EN + FR + AR) ──────────────────────────────
        var categoryMap = new Dictionary<string, string>
        {
            // English
            { "invoice", "Invoice" }, { "invoices", "Invoice" },
            { "contract", "Contract" }, { "contracts", "Contract" },
            { "report", "Report" }, { "reports", "Report" },
            { "letter", "Letter" }, { "letters", "Letter" },
            { "memo", "Memo" }, { "presentation", "Presentation" },
            // French
            { "facture", "Invoice" }, { "factures", "Invoice" },
            { "contrat", "Contract" }, { "contrats", "Contract" },
            { "rapport", "Report" }, { "rapports", "Report" },
            { "lettre", "Letter" }, { "lettres", "Letter" },
            { "courrier", "Letter" }, { "devis", "Invoice" },
            { "note", "Memo" }, { "présentation", "Presentation" },
            // Arabic
            { "فاتورة", "Invoice" }, { "فواتير", "Invoice" },
            { "عقد", "Contract" }, { "عقود", "Contract" },
            { "تقرير", "Report" }, { "تقارير", "Report" },
            { "رسالة", "Letter" }, { "رسائل", "Letter" },
            { "مذكرة", "Memo" }, { "عرض", "Presentation" }
        };

        foreach (var (keyword, category) in categoryMap)
            if (lower.Contains(keyword) || text.Contains(keyword))
                entities.Add($"CATEGORY:{category}");

        // ── Year ────────────────────────────────────────────────────────────
        var yearMatch = System.Text.RegularExpressions.Regex.Match(text, @"\b(20\d{2})\b");
        if (yearMatch.Success)
            entities.Add($"YEAR:{yearMatch.Value}");

        return entities.Distinct().ToList();
    }

    // ── Filters (dates, file types) ───────────────────────────────────────────

    private static Dictionary<string, string> ExtractFilters(string query)
    {
        var filters = new Dictionary<string, string>();
        var lower   = query.ToLower();
        var now     = DateTime.UtcNow;

        // ── Year filter ──────────────────────────────────────────────────────
        var yearMatch = System.Text.RegularExpressions.Regex.Match(query, @"\b(20\d{2})\b");
        if (yearMatch.Success)
        {
            var year = int.Parse(yearMatch.Value);
            filters["fromDate"] = new DateTime(year, 1, 1).ToString("o");
            filters["toDate"]   = new DateTime(year, 12, 31, 23, 59, 59).ToString("o");
        }

        // ── File type filter ─────────────────────────────────────────────────
        var fileTypes = new[] { "pdf", "doc", "docx", "xls", "xlsx", "jpg", "jpeg", "png", "txt" };
        foreach (var ft in fileTypes)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(lower, $@"\b{ft}s?\b"))
            {
                filters["filetype"] = ft;
                break;
            }
        }

        // ── Relative date ranges (EN + FR + AR) ──────────────────────────────
        // English
        if      (lower.Contains("last month"))    SetLastMonth(filters, now);
        else if (lower.Contains("last year"))     SetLastYear(filters, now);
        else if (lower.Contains("this month"))    SetThisMonth(filters, now);
        else if (lower.Contains("this year"))     SetThisYear(filters, now);
        else if (lower.Contains("last week"))     SetLastWeek(filters, now);
        else if (lower.Contains("today"))         SetToday(filters, now);
        else if (lower.Contains("yesterday"))     SetYesterday(filters, now);
        // French
        else if (lower.Contains("mois dernier"))  SetLastMonth(filters, now);
        else if (lower.Contains("année dernière") || lower.Contains("l'an dernier")) SetLastYear(filters, now);
        else if (lower.Contains("ce mois"))       SetThisMonth(filters, now);
        else if (lower.Contains("cette année"))   SetThisYear(filters, now);
        else if (lower.Contains("semaine dernière")) SetLastWeek(filters, now);
        else if (lower.Contains("aujourd'hui"))   SetToday(filters, now);
        else if (lower.Contains("hier"))          SetYesterday(filters, now);
        // Arabic
        else if (query.Contains("الشهر الماضي")) SetLastMonth(filters, now);
        else if (query.Contains("السنة الماضية") || query.Contains("العام الماضي")) SetLastYear(filters, now);
        else if (query.Contains("هذا الشهر"))    SetThisMonth(filters, now);
        else if (query.Contains("هذه السنة"))    SetThisYear(filters, now);
        else if (query.Contains("الأسبوع الماضي")) SetLastWeek(filters, now);
        else if (query.Contains("اليوم"))         SetToday(filters, now);
        else if (query.Contains("أمس"))           SetYesterday(filters, now);

        // ── Month name filter (EN + FR) ───────────────────────────────────────
        if (!filters.ContainsKey("fromDate"))
        {
            var months = new Dictionary<string, int>
            {
                // English
                {"january",1},{"february",2},{"march",3},{"april",4},
                {"may",5},{"june",6},{"july",7},{"august",8},
                {"september",9},{"october",10},{"november",11},{"december",12},
                // French
                {"janvier",1},{"février",2},{"fevrier",2},{"mars",3},
                {"avril",4},{"mai",5},{"juin",6},{"juillet",7},
                {"août",8},{"aout",8},{"septembre",9},{"octobre",10},
                {"novembre",11},{"décembre",12},{"decembre",12}
            };

            foreach (var (name, num) in months)
            {
                if (!lower.Contains(name)) continue;

                // Try to find a year; default to current year
                var yearM = System.Text.RegularExpressions.Regex.Match(query, @"\b(20\d{2})\b");
                var yr = yearM.Success ? int.Parse(yearM.Value) : now.Year;

                filters["fromDate"] = new DateTime(yr, num, 1).ToString("o");
                filters["toDate"]   = new DateTime(yr, num, DateTime.DaysInMonth(yr, num), 23, 59, 59).ToString("o");
                break;
            }
        }

        return filters;
    }

    // ── Date helpers ──────────────────────────────────────────────────────────

    private static void SetLastMonth(Dictionary<string, string> f, DateTime now)
    {
        var first = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
        f["fromDate"] = first.ToString("o");
        f["toDate"]   = first.AddMonths(1).AddSeconds(-1).ToString("o");
    }

    private static void SetLastYear(Dictionary<string, string> f, DateTime now)
    {
        f["fromDate"] = new DateTime(now.Year - 1, 1, 1).ToString("o");
        f["toDate"]   = new DateTime(now.Year - 1, 12, 31, 23, 59, 59).ToString("o");
    }

    private static void SetThisMonth(Dictionary<string, string> f, DateTime now)
    {
        f["fromDate"] = new DateTime(now.Year, now.Month, 1).ToString("o");
        f["toDate"]   = now.ToString("o");
    }

    private static void SetThisYear(Dictionary<string, string> f, DateTime now)
    {
        f["fromDate"] = new DateTime(now.Year, 1, 1).ToString("o");
        f["toDate"]   = now.ToString("o");
    }

    private static void SetLastWeek(Dictionary<string, string> f, DateTime now)
    {
        f["fromDate"] = now.AddDays(-7).Date.ToString("o");
        f["toDate"]   = now.ToString("o");
    }

    private static void SetToday(Dictionary<string, string> f, DateTime now)
    {
        f["fromDate"] = now.Date.ToString("o");
        f["toDate"]   = now.ToString("o");
    }

    private static void SetYesterday(Dictionary<string, string> f, DateTime now)
    {
        var y = now.AddDays(-1).Date;
        f["fromDate"] = y.ToString("o");
        f["toDate"]   = y.AddDays(1).AddSeconds(-1).ToString("o");
    }

    // ── Intent + query processing ─────────────────────────────────────────────

    private static QueryIntent DetermineIntent(string query)
    {
        var lower = query.ToLower();
        if (new[] { "find","search","trouver","chercher","ابحث","اعثر" }.Any(lower.Contains)) return QueryIntent.Find;
        if (new[] { "list","all","liste","tous","كل","جميع" }.Any(lower.Contains))            return QueryIntent.List;
        if (new[] { "filter","where","filtrer","فقط","حيث" }.Any(lower.Contains))             return QueryIntent.Filter;
        if (new[] { "compare","difference","vs","comparer","مقارنة" }.Any(lower.Contains))    return QueryIntent.Compare;
        return QueryIntent.Search;
    }

    private static string ProcessQuery(string query, List<string> keywords)
    {
        // Remove command/question phrases; leave domain terms
        var commands = new[]
        {
            "show me","find me","get me","search for","look for","give me",
            "montre-moi","montrer moi","trouve-moi","trouver moi","cherche",
            "recherche","affiche","afficher","donner moi","donne-moi",
            "أعطني","أظهر لي","ابحث عن","جد لي","أرني"
        };

        var processed = query;
        foreach (var cmd in commands)
            processed = processed.Replace(cmd, "", StringComparison.OrdinalIgnoreCase);

        return processed.Trim();
    }

    // ── Ollama DTOs ───────────────────────────────────────────────────────────

    private class OllamaEmbedRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("input")]
        public string Input { get; set; } = string.Empty;
    }

    private class OllamaEmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public List<float[]>? Embeddings { get; set; }
    }
}
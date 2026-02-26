using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GED.Infrastructure.Services;

/// <summary>
/// RAG (Retrieval Augmented Generation) Service.
///
/// Pipeline:
///   1. Receive a natural-language user query
///   2. Search OpenSearch for the top-N most relevant document chunks
///   3. Build a context prompt from the retrieved excerpts
///   4. Call Ollama (local LLM) to generate a synthetic answer
///   5. Return the answer + the source documents with relevant excerpts
///
/// This is the core AI feature requested in the cahier des charges:
///   "Génération de réponses synthétiques avec références aux documents sources"
/// </summary>
public class RagService : IRagService
{
    private readonly ISearchService _searchService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<RagService> _logger;

    private readonly string _llmEndpoint;
    private readonly string _llmModel;
    private readonly bool _enabled;
    private readonly int _topK;
    private readonly int _maxContextChars;

    public RagService(
        ISearchService searchService,
        HttpClient httpClient,
        ILogger<RagService> logger,
        IConfiguration configuration)
    {
        _searchService  = searchService;
        _httpClient     = httpClient;
        _logger         = logger;
        _llmEndpoint    = configuration["NLP:LlmApiEndpoint"] ?? "http://localhost:11434/api/generate";
        _llmModel       = configuration["NLP:Model"] ?? "llama3.2";
        _enabled        = configuration.GetValue<bool>("NLP:Enabled", true);
        _topK           = configuration.GetValue<int>("RAG:TopK", 5);
        _maxContextChars = configuration.GetValue<int>("RAG:MaxContextChars", 6000);
    }

    /// <summary>
    /// Full RAG pipeline: search → build context → generate answer → return with sources.
    /// </summary>
    public async Task<RagResponse> AskAsync(
        RagRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return new RagResponse
            {
                Answer  = "Le module IA est désactivé. Veuillez activer NLP:Enabled dans la configuration.",
                Sources = new List<RagSource>(),
                Query   = request.Query
            };
        }

        var startTime = DateTime.UtcNow;

        _logger.LogInformation("🤖 RAG pipeline starting for query: '{Query}'", request.Query);

        // ── Step 1: Retrieve relevant documents from OpenSearch ───────────────
        var searchRequest = new SearchRequest
        {
            Query              = request.Query,
            SearchType         = SearchType.Natural,
            Page               = 1,
            PageSize           = _topK,
            IncludeOcrContent  = true,
            IncludeSuggestions = false,
            Categories         = request.Categories,
            ContentTypes       = request.ContentTypes,
            FromDate           = request.FromDate,
            ToDate             = request.ToDate
        };

        SearchResult searchResult;
        try
        {
            searchResult = await _searchService.SearchAsync(searchRequest, cancellationToken);
            _logger.LogInformation(
                "🔍 RAG retrieved {Count} documents (total={Total})",
                searchResult.Documents.Count, searchResult.TotalResults);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG search step failed");
            return ErrorResponse(request.Query, "La recherche de documents a échoué.");
        }

        if (!searchResult.Documents.Any())
        {
            return new RagResponse
            {
                Answer  = "Aucun document pertinent n'a été trouvé pour répondre à votre question.",
                Sources = new List<RagSource>(),
                Query   = request.Query,
                TotalDocumentsSearched = searchResult.TotalResults
            };
        }

        // ── Step 2: Build context from retrieved document excerpts ────────────
        var sources  = new List<RagSource>();
        var contextBuilder = new StringBuilder();
        int charsUsed = 0;

        foreach (var doc in searchResult.Documents)
        {
            // Extract the most relevant excerpt for this document
            var excerpt = ExtractBestExcerpt(doc);
            if (string.IsNullOrWhiteSpace(excerpt)) continue;

            // Truncate excerpt if adding it would exceed context limit
            var remaining = _maxContextChars - charsUsed;
            if (remaining <= 100) break;

            var truncated = excerpt.Length > remaining
                ? excerpt[..remaining] + "…"
                : excerpt;

            contextBuilder.AppendLine($"--- Document {sources.Count + 1}: {doc.Title} ---");
            if (doc.DocumentDate.HasValue)
                contextBuilder.AppendLine($"Date: {doc.DocumentDate.Value:yyyy-MM-dd}");
            if (!string.IsNullOrEmpty(doc.Category))
                contextBuilder.AppendLine($"Catégorie: {doc.Category}");
            contextBuilder.AppendLine(truncated);
            contextBuilder.AppendLine();

            charsUsed += truncated.Length + 100; // +100 for the header lines

            sources.Add(new RagSource
            {
                DocumentId   = doc.Id,
                Title        = doc.Title,
                Category     = doc.Category,
                DocumentDate = doc.DocumentDate,
                CreatedAt    = doc.CreatedAt,
                FileName     = doc.FileName,
                ContentType  = doc.ContentType,
                RelevanceScore = doc.Score,
                Excerpt      = truncated.Length > 400 ? truncated[..400] + "…" : truncated,
                Highlights   = doc.Highlights ?? new List<string>()
            });
        }

        _logger.LogInformation(
            "📄 Built context from {Count} sources ({Chars} chars)",
            sources.Count, charsUsed);

        // ── Step 3: Generate synthetic answer via Ollama ──────────────────────
        var context = contextBuilder.ToString();
        string answer;

        try
        {
            answer = await GenerateAnswerAsync(request.Query, context, request.Language, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG generation step failed");
            // Degrade gracefully: return search results without AI answer
            answer = BuildFallbackAnswer(sources);
        }

        var elapsed = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

        _logger.LogInformation(
            "✅ RAG pipeline completed in {Ms}ms: {SourceCount} sources, answer={Chars} chars",
            elapsed, sources.Count, answer.Length);

        return new RagResponse
        {
            Query                  = request.Query,
            Answer                 = answer,
            Sources                = sources,
            SearchTimeMs           = elapsed,
            TotalDocumentsSearched = searchResult.TotalResults,
            ModelUsed              = _llmModel
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<string> GenerateAnswerAsync(
        string query,
        string context,
        string language,
        CancellationToken cancellationToken)
    {
        // Detect response language preference
        var lang = string.IsNullOrEmpty(language) ? "fr" : language.ToLower();
        var langInstruction = lang switch
        {
            "en" => "Respond in English.",
            "ar" => "أجب باللغة العربية.",
            _    => "Réponds en français."
        };

        var prompt = $@"Tu es un assistant intelligent spécialisé dans la gestion électronique de documents (GED).
Tu as accès aux extraits de documents suivants, récupérés depuis la base documentaire.

DOCUMENTS PERTINENTS :
{context}

QUESTION DE L'UTILISATEUR :
{query}

INSTRUCTIONS :
- Réponds de manière précise et synthétique en te basant UNIQUEMENT sur les documents fournis.
- Si les documents ne contiennent pas suffisamment d'informations pour répondre, indique-le clairement.
- Cite les documents pertinents dans ta réponse (exemple : ""Selon le document 1 (titre)..."" ).
- Ne fabrique aucune information qui ne figure pas dans les documents.
- {langInstruction}

RÉPONSE :";

        var requestBody = new
        {
            model       = _llmModel,
            prompt      = prompt,
            stream      = false,
            temperature = 0.3,  // Low temp for factual responses
            options     = new { num_predict = 1024 }
        };

        _logger.LogDebug("Calling Ollama for RAG generation at {Endpoint}", _llmEndpoint);

        var response = await _httpClient.PostAsJsonAsync(_llmEndpoint, requestBody, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Ollama returned {Status}: {Body}", response.StatusCode, errorBody);
            throw new Exception($"Ollama error: {response.StatusCode}");
        }

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken);

        var answer = result?.Response?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(answer))
            throw new Exception("Ollama returned empty response");

        return answer;
    }

    /// <summary>
    /// Extract the best text excerpt from a search hit.
    /// Priority: highlights → extracted text → description.
    /// </summary>
    private static string ExtractBestExcerpt(DocumentSearchHit doc)
    {
        // Prefer search highlights (already relevant snippets)
        if (doc.Highlights?.Any() == true)
            return string.Join(" … ", doc.Highlights.Take(3));

        // Fall back to description
        if (!string.IsNullOrWhiteSpace(doc.Description))
            return doc.Description;

        return string.Empty;
    }

    /// <summary>
    /// Fallback answer when the LLM is unavailable — lists found documents.
    /// </summary>
    private static string BuildFallbackAnswer(List<RagSource> sources)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Le module de génération IA est temporairement indisponible.");
        sb.AppendLine("Voici les documents les plus pertinents trouvés :");
        sb.AppendLine();
        foreach (var (src, i) in sources.Select((s, i) => (s, i + 1)))
        {
            sb.AppendLine($"{i}. {src.Title}");
            if (!string.IsNullOrEmpty(src.Category))
                sb.AppendLine($"   Catégorie : {src.Category}");
            if (src.DocumentDate.HasValue)
                sb.AppendLine($"   Date : {src.DocumentDate.Value:dd/MM/yyyy}");
        }
        return sb.ToString();
    }

    private static RagResponse ErrorResponse(string query, string message) => new()
    {
        Query   = query,
        Answer  = message,
        Sources = new List<RagSource>()
    };

    private class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }
    }
}

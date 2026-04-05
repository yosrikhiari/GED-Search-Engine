using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GED.Infrastructure.Services;

/// <summary>
/// RAG (Retrieval Augmented Generation) service for AI-powered question answering.
/// 
/// <para>
/// The service retrieves relevant document chunks based on the user's query and
/// generates a natural language answer using a local LLM (Ollama).
/// </para>
/// 
/// <para>
/// Security: The user's identity (UserId, UserRole, AllowedCategories) is forwarded
/// to every internal SearchRequest so OpenSearch's ACL filter applies to RAG
/// context retrieval as well. Users can only get answers from documents they
/// are allowed to see.
/// </para>
/// 
/// <para>
/// RAG Pipeline:
/// <list type="number">
///   <item>
///     <term>Context building</term>
///     <description>
///       Searches for relevant chunks using semantic similarity (kNN).
///       Falls back to full-document search if no chunks found.
///     </description>
///   </item>
///   <item>
///     <term>ACL enforcement</term>
///     <description>
///       Only retrieves chunks from accessible documents.
///     </description>
///   </item>
///   <item>
///     <term>Prompt construction</term>
///     <description>
///       Builds prompt with retrieved context and user question.
///     </description>
///   </item>
///   <item>
///     <term>Answer generation</term>
///     <description>
///       Calls Ollama to generate answer from context.
///     </description>
///   </item>
/// </list>
/// </para>
/// 
/// <para>
/// Supports both streaming and non-streaming response modes.
/// </para>
/// </summary>
public class RagService : IRagService
{
    private readonly ISearchService         _searchService;
    private readonly IUserContext           _userContext;
    private readonly OpenSearchService      _openSearch;
    private readonly IChunkRerankerService _reranker;
    private readonly IQueryClassifierService _classifier;
    private readonly HttpClient             _httpClient;
    private readonly ILogger<RagService>     _logger;

    /// <summary>
    /// Ollama API endpoint for text generation.
    /// </summary>
    private readonly string _llmEndpoint;

    /// <summary>
    /// Ollama model name for answer generation.
    /// </summary>
    private readonly string _llmModel;

    /// <summary>
    /// Whether the RAG service is enabled.
    /// </summary>
    private readonly bool   _enabled;

    /// <summary>
    /// Number of top chunks to retrieve for context.
    /// </summary>
    private readonly int    _topK;

    /// <summary>
    /// Maximum characters to include in context (to fit LLM context window).
    /// </summary>
    private readonly int    _maxContextChars;

    /// <summary>
    /// Minimum confidence threshold for chunk inclusion (configurable).
    /// </summary>
    private readonly float _confidenceThreshold;

    /// <summary>
    /// Whether BM25 fallback is enabled for chunk search.
    /// </summary>
    private readonly bool _enableBm25Fallback;

    /// <summary>
    /// Whether chunk reranking is enabled.
    /// </summary>
    private readonly bool _enableReranking;

    /// <summary>
    /// Whether query classification is enabled.
    /// </summary>
    private readonly bool _enableQueryClassification;

    /// <summary>
    /// Max chunks per document for comparison queries.
    /// </summary>
    private readonly int _comparisonMaxChunksPerDoc;

    /// <summary>
    /// Initializes a new instance of <see cref="RagService"/>.
    /// </summary>
    /// <param name="searchService">Search service for document retrieval.</param>
    /// <param name="openSearch">OpenSearch service for chunk search.</param>
    /// <param name="userContext">User context for ACL enforcement.</param>
    /// <param name="reranker">Chunk reranker service.</param>
    /// <param name="classifier">Query classifier service.</param>
    /// <param name="httpClient">HTTP client for Ollama API.</param>
    /// <param name="logger">Logger for RAG events.</param>
    /// <param name="configuration">Application configuration.</param>
    public RagService(
        ISearchService         searchService,
        OpenSearchService      openSearch,
        IUserContext           userContext,
        IChunkRerankerService  reranker,
        IQueryClassifierService classifier,
        HttpClient             httpClient,
        ILogger<RagService>    logger,
        IConfiguration         configuration)
    {
        _searchService   = searchService;
        _openSearch      = openSearch;
        _userContext     = userContext;
        _reranker        = reranker;
        _classifier      = classifier;
        _httpClient      = httpClient;
        _logger          = logger;
        _llmEndpoint     = configuration["NLP:LlmApiEndpoint"] ?? "http://localhost:11434/api/generate";
        _llmModel        = configuration["NLP:Model"] ?? "llama3.2";
        _enabled         = configuration.GetValue<bool>("NLP:Enabled", true);
        _topK            = configuration.GetValue<int>("RAG:TopK", 5);
        _maxContextChars = configuration.GetValue<int>("RAG:MaxContextChars", 6000);
        _confidenceThreshold = configuration.GetValue<float>("RAG:ConfidenceThreshold", 0.45f);
        _enableBm25Fallback = configuration.GetValue<bool>("RAG:EnableBm25Fallback", true);
        _enableReranking = configuration.GetValue<bool>("RAG:EnableReranking", false);
        _enableQueryClassification = configuration.GetValue<bool>("RAG:EnableQueryClassification", true);
        _comparisonMaxChunksPerDoc = configuration.GetValue<int>("RAG:QueryClassification:ComparisonMaxChunksPerDoc", 2);
    }

    /// <inheritdoc />
    public async Task<RagResponse> AskAsync(
        RagRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
            return new RagResponse
            {
                Answer  = "Le module IA est désactivé. Veuillez activer NLP:Enabled dans la configuration.",
                Sources = new List<RagSource>(),
                Query   = request.Query
            };

        var startTime = DateTime.UtcNow;
        _logger.LogInformation("🤖 RAG pipeline starting for query: '{Query}'", request.Query);

        // Build context from relevant documents
        var (effectiveCategories, chunkHits, sources, contextBuilder, charsUsed, earlyExit) =
            await BuildContextAsync(request, cancellationToken);

        if (earlyExit != null) return earlyExit;

        // No relevant documents found
        if (!sources.Any())
            return new RagResponse
            {
                Answer  = "Aucun document pertinent n'a été trouvé pour répondre à votre question.",
                Sources = new List<RagSource>(),
                Query   = request.Query
            };

        // Generate answer from context
        string answer;
        try
        {
            answer = await GenerateAnswerAsync(
                request.Query, contextBuilder.ToString(), request.Language, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG generation step failed");
            answer = BuildFallbackAnswer(sources);
        }

        var elapsed = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
        _logger.LogInformation("✅ RAG pipeline completed in {Ms}ms: {SourceCount} sources",
            elapsed, sources.Count);

        return new RagResponse
        {
            Query                  = request.Query,
            Answer                = answer,
            Sources               = sources,
            SearchTimeMs          = elapsed,
            TotalDocumentsSearched = sources.Count,
            ModelUsed             = _llmModel
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> AskStreamAsync(
        RagRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            yield return "Le module IA est désactivé. Veuillez activer NLP:Enabled dans la configuration.";
            yield break;
        }

        var (_, _, sources, contextBuilder, _, earlyExit) =
            await BuildContextAsync(request, cancellationToken);

        if (earlyExit != null) { yield return earlyExit.Answer; yield break; }
        if (!sources.Any())   { yield return "Aucun document pertinent n'a été trouvé."; yield break; }

        var prompt      = BuildPrompt(request.Query, contextBuilder.ToString(), request.Language);
        var requestBody = new
        {
            model       = _llmModel,
            prompt      = prompt,
            stream      = true,
            temperature = 0.3,
            options     = new { num_predict = 1024 }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, _llmEndpoint)
        {
            Content = JsonContent.Create(requestBody)
        };

        string? fallbackAnswer  = null;
        HttpResponseMessage? response = null;
        try
        {
            response = await _httpClient.SendAsync(
                req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG stream: Ollama request failed");
            fallbackAnswer = BuildFallbackAnswer(sources);
        }

        if (fallbackAnswer != null) { yield return fallbackAnswer; yield break; }

        if (response is null)
            throw new InvalidOperationException("HTTP response was null after success check.");

        // Stream response tokens
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;
            OllamaStreamChunk? chunk = null;
            try { chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line); } catch { continue; }
            if (chunk?.Response != null) yield return chunk.Response;
            if (chunk?.Done == true) break;
        }
    }

    // ── Shared context builder ────────────────────────────────────────────────

    /// <summary>
    /// Builds context by retrieving relevant document chunks.
    /// </summary>
    private async Task<(
        List<string>?        effectiveCategories,
        List<ChunkSearchHit> chunkHits,
        List<RagSource>      sources,
        StringBuilder        contextBuilder,
        int                  charsUsed,
        RagResponse?         earlyExit)>
        BuildContextAsync(RagRequest request, CancellationToken cancellationToken)
    {
        // Apply category-based ACL
        List<string>? allowedCategories = null;
        if (!string.IsNullOrEmpty(request.Username))
        {
            allowedCategories = _userContext.GetAllowedCategories(request.Username);
            if (allowedCategories != null)
            {
                // Intersect with requested categories (if any)
                allowedCategories = request.Categories == null
                    ? allowedCategories
                    : request.Categories
                        .Intersect(allowedCategories, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                _logger.LogInformation(
                    "RAG ACL applied for '{User}': categories restricted to [{Cats}]",
                    request.Username, string.Join(", ", allowedCategories));
            }
        }

        var effectiveCategories = allowedCategories ?? request.Categories;

        // Query classification to determine retrieval strategy
        var classification = await _classifier.ClassifyAsync(request.Query, cancellationToken);
        var effectiveTopK = classification?.RecommendedTopK ?? _topK;
        var effectiveThreshold = classification?.RecommendedConfidenceThreshold ?? _confidenceThreshold;

        _logger.LogInformation(
            "📊 Query classified: type={Type}, confidence={Conf:F2}, topK={TopK}, threshold={Threshold}{Fallback}",
            classification?.QueryType ?? "unknown",
            classification?.Confidence ?? 1.0f,
            effectiveTopK,
            effectiveThreshold,
            classification?.IsAmbiguous == true ? " (fallback to factual)" : "");

        // Search for relevant chunks
        List<ChunkSearchHit> chunkHits = new();
        try
        {
            var userRole = request.UserRole ?? _userContext.GetType()
                .GetProperty("Role")?.GetValue(_userContext)?.ToString() ?? "User";
            
            chunkHits = await _openSearch.SearchChunksAsync(
                request.Query, topK: effectiveTopK,
                categories: effectiveCategories,
                documentIds: request.DocumentIds,
                userId: request.UserId ?? request.Username,
                userRole: userRole,
                userAllowedCategories: effectiveCategories,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "🔍 RAG chunk search: {Count} chunk hits, scores: [{Scores}]",
                chunkHits.Count,
                string.Join(", ", chunkHits.Select(c => $"{c.Title?.Split(':').LastOrDefault()?.Trim() ?? "?"}:{c.Score:F2}")));

            // Log detailed chunk info for debugging
            foreach (var (chunk, idx) in chunkHits.Select((c, i) => (c, i)))
            {
                _logger.LogDebug(
                    "Chunk {Idx}: DocId={DocId}, ChunkIdx={ChunkIdx}, Score={Score:F3}, Title={Title}, TextLen={TextLen}",
                    idx, chunk.DocumentId, chunk.ChunkIndex, chunk.Score,
                    chunk.Title ?? "?", chunk.Text?.Length ?? 0);
            }

            // Re-rank chunks for better relevance (if enabled and we have multiple chunks)
            if (_enableReranking && chunkHits.Count > 1 && _reranker.IsAvailable)
            {
                _logger.LogInformation("🔄 Re-ranking {Count} chunks for improved relevance...", chunkHits.Count);
                var preRerankScores = chunkHits.Select(c => c.Score).ToList();
                chunkHits = await _reranker.ReRankAsync(request.Query, chunkHits, cancellationToken);
                _logger.LogInformation(
                    "✅ Re-ranking complete. Score change: [{Pre}] → [{Post}]",
                    string.Join(", ", preRerankScores.Select(s => $"{s:F2}")),
                    string.Join(", ", chunkHits.Select(c => $"{c.Score:F2}")));
            }

            // For comparison queries, ensure chunks come from different documents
            if (classification?.QueryType == "comparison" && chunkHits.Count > 1)
            {
                _logger.LogInformation("🔄 Applying document diversity for comparison query (max {Max} chunks/doc)",
                    _comparisonMaxChunksPerDoc);
                chunkHits = EnsureDocumentDiversity(chunkHits, _comparisonMaxChunksPerDoc, effectiveTopK);
            }

            // Check confidence threshold
            bool hasConfidentChunks = chunkHits.Any(c => c.Score >= effectiveThreshold);
            if (chunkHits.Any() && !hasConfidentChunks)
            {
                _logger.LogWarning(
                    "RAG: all {Count} chunks below confidence {T} — low-confidence notice",
                    chunkHits.Count, effectiveThreshold);

                return (effectiveCategories, chunkHits, new List<RagSource>(), new StringBuilder(), 0,
                    new RagResponse
                    {
                        Query       = request.Query,
                        Answer      = "Je n'ai pas trouvé d'informations suffisamment fiables dans les documents pour répondre à cette question avec certitude.",
                        Sources     = new List<RagSource>(),
                        IsConfident = false
                    });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chunk search failed — will fall back to full-doc search");
        }

        // Build context from chunks
        var sources        = new List<RagSource>();
        var contextBuilder = new StringBuilder();
        int charsUsed      = 0;

        if (chunkHits.Any())
        {
            var seenDocIds = new HashSet<Guid>();
            foreach (var chunk in chunkHits)
            {
                // Respect max context length
                if (charsUsed >= _maxContextChars) break;
                var remaining = _maxContextChars - charsUsed;
                var text      = chunk.Text.Length > remaining ? chunk.Text[..remaining] + "…" : chunk.Text;

                // Format chunk for context
                contextBuilder.AppendLine($"--- Document : {chunk.Title} (extrait {chunk.ChunkIndex + 1}) ---");
                if (chunk.DocumentDate.HasValue)
                    contextBuilder.AppendLine($"Date : {chunk.DocumentDate.Value:yyyy-MM-dd}");
                if (!string.IsNullOrEmpty(chunk.Category))
                    contextBuilder.AppendLine($"Catégorie : {chunk.Category}");
                contextBuilder.AppendLine(text);
                contextBuilder.AppendLine();
                charsUsed += text.Length + 80;

                // Track unique documents
                if (seenDocIds.Add(chunk.DocumentId))
                    sources.Add(new RagSource
                    {
                        DocumentId     = chunk.DocumentId,
                        Title          = chunk.Title,
                        Category       = chunk.Category,
                        DocumentDate   = chunk.DocumentDate,
                        FileName       = chunk.FileName,
                        ContentType    = chunk.ContentType,
                        RelevanceScore = chunk.Score,
                        Excerpt        = text.Length > 400 ? text[..400] + "…" : text,
                        Highlights     = new List<string>()
                    });
            }

            _logger.LogInformation("📄 Built chunk context: {DocCount} docs, {Chars} chars",
                sources.Count, charsUsed);
        }
        else
        {
            // Fallback to full-document search
            _logger.LogInformation("⚠️  No chunk hits — falling back to full-document search");

            var searchRequest = new SearchRequest
            {
                Query              = request.Query,
                SearchType         = SearchType.Natural,
                Page               = 1,
                PageSize           = _topK,
                IncludeOcrContent  = true,
                IncludeSuggestions = false,
                Categories         = effectiveCategories,
                ContentTypes       = request.ContentTypes,
                FromDate           = request.FromDate,
                ToDate             = request.ToDate,
                DocumentIds        = request.DocumentIds,
                UserId             = request.UserId,
                UserRole           = request.UserRole,
                UserAllowedCategories = request.UserAllowedCategories
            };

            SearchResult searchResult;
            try
            {
                searchResult = await _searchService.SearchAsync(searchRequest, cancellationToken);
                _logger.LogInformation(
                    "🔍 RAG full-doc fallback: {Count} documents (total={Total})",
                    searchResult.Documents.Count, searchResult.TotalResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RAG full-doc fallback search also failed");
                return (effectiveCategories, chunkHits, sources, contextBuilder, charsUsed,
                    new RagResponse
                    {
                        Query   = request.Query,
                        Answer  = "La recherche de documents a échoué.",
                        Sources = new List<RagSource>()
                    });
            }

            if (!searchResult.Documents.Any())
                return (effectiveCategories, chunkHits, sources, contextBuilder, charsUsed,
                    new RagResponse
                    {
                        Answer                 = "Aucun document pertinent n'a été trouvé pour répondre à votre question.",
                        Sources                = new List<RagSource>(),
                        Query                  = request.Query,
                        TotalDocumentsSearched = searchResult.TotalResults
                    });

            // Build context from full documents
            foreach (var doc in searchResult.Documents)
            {
                var excerpt = ExtractBestExcerpt(doc);
                if (string.IsNullOrWhiteSpace(excerpt)) continue;
                var remaining = _maxContextChars - charsUsed;
                if (remaining <= 100) break;

                // Allocate more context to higher-scoring documents
                var scoreFraction = doc.Score > 0 ? Math.Min(doc.Score * 1.5f, 1.0f) : 0.5f;
                var budget        = Math.Max((int)(remaining * scoreFraction), 300);
                var truncated     = excerpt.Length > budget ? excerpt[..budget] + "…" : excerpt;

                contextBuilder.AppendLine($"--- Document {sources.Count + 1} : {doc.Title} ---");
                if (doc.DocumentDate.HasValue)
                    contextBuilder.AppendLine($"Date : {doc.DocumentDate.Value:yyyy-MM-dd}");
                if (!string.IsNullOrEmpty(doc.Category))
                    contextBuilder.AppendLine($"Catégorie : {doc.Category}");
                contextBuilder.AppendLine(truncated);
                contextBuilder.AppendLine();
                charsUsed += truncated.Length + 100;

                sources.Add(new RagSource
                {
                    DocumentId     = doc.Id,
                    Title          = doc.Title,
                    Category       = doc.Category,
                    DocumentDate   = doc.DocumentDate,
                    CreatedAt      = doc.CreatedAt,
                    FileName       = doc.FileName,
                    ContentType    = doc.ContentType,
                    RelevanceScore = doc.Score,
                    Excerpt        = truncated.Length > 400 ? truncated[..400] + "…" : truncated,
                    Highlights     = doc.Highlights ?? new List<string>()
                });
            }

            _logger.LogInformation("📄 Built full-doc context: {Count} sources, {Chars} chars",
                sources.Count, charsUsed);
        }

        return (effectiveCategories, chunkHits, sources, contextBuilder, charsUsed, null);
    }

    // ── Prompt builder ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the full prompt for answer generation.
    /// </summary>
    private string BuildPrompt(string query, string context, string language)
    {
        var lang = string.IsNullOrEmpty(language) ? "fr" : language.ToLower();

        var (systemPrompt, formatInstructions, languageInstruction) = GetLanguageConfig(lang);

        return $@"{systemPrompt}

DOCUMENTS RETRIEVED FROM KNOWLEDGE BASE:
{context}

USER QUESTION:
{query}

INSTRUCTIONS:
- Answer precisely and concisely based ONLY on the provided documents.
- If the documents don't contain enough information to answer, clearly state that.
- Cite the relevant documents in your answer (e.g., ""According to Document 1 (title)..."".
- Do NOT fabricate any information not present in the documents.
- {languageInstruction}

{formatInstructions}

ANSWER:";
    }

    /// <summary>
    /// Gets language-specific prompt configuration.
    /// </summary>
    private static (string systemPrompt, string formatInstructions, string languageInstruction) GetLanguageConfig(string lang)
    {
        return lang switch
        {
            "ar" => (
                @"أنت مساعد ذكي متخصص في إدارة الوثائق الإلكترونية (GED).",
                @"قدم إجابة موجزة ومنظمة.",
                "أجب باللغة العربية."
            ),
            "en" => (
                @"You are an intelligent assistant specialized in Electronic Document Management (GED).",
                @"Provide a concise and well-organized answer.",
                "Answer in English."
            ),
            _ => (
                @"Tu es un assistant intelligent spécialisé dans la gestion électronique de documents (GED).",
                @"Fournis une réponse concise et bien organisée.",
                "Réponds en français."
            )
        };
    }

    // ── Non-streaming Ollama call ─────────────────────────────────────────────

    /// <summary>
    /// Generates an answer from context using Ollama.
    /// </summary>
    private async Task<string> GenerateAnswerAsync(
        string query, string context, string language, CancellationToken cancellationToken)
    {
        var prompt      = BuildPrompt(query, context, language);
        var requestBody = new
        {
            model       = _llmModel,
            prompt      = prompt,
            stream      = false,
            temperature = 0.3,
            options     = new { num_predict = 1024 }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(120));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(_llmEndpoint, requestBody, cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("RAG generation timed out after 120s");
            throw new TimeoutException("LLM generation timed out. Please try a simpler query.");
        }

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

    // ── Static helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the best excerpt from a document for context.
    /// Prefers highlights, then OCR text, then extracted text, then description.
    /// </summary>
    private static string ExtractBestExcerpt(DocumentSearchHit doc)
    {
        if (doc.Highlights?.Any() == true)
            return string.Join(" … ", doc.Highlights.Take(3));
        if (!string.IsNullOrWhiteSpace(doc.OcrText))
            return doc.OcrText.Length > 2000 ? doc.OcrText[..2000] : doc.OcrText;
        if (!string.IsNullOrWhiteSpace(doc.ExtractedText))
            return doc.ExtractedText.Length > 2000 ? doc.ExtractedText[..2000] : doc.ExtractedText;
        if (!string.IsNullOrWhiteSpace(doc.Description))
            return doc.Description;
        return string.Empty;
    }

    /// <summary>
    /// Builds a fallback answer listing relevant documents when LLM fails.
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

    // ── Ollama DTOs ──────────────────────────────────────────────────────────

    private class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }
    }

    /// <summary>
    /// Ensures chunks are distributed across multiple documents for comparison queries.
    /// Limits the number of chunks taken from each document.
    /// </summary>
    private static List<ChunkSearchHit> EnsureDocumentDiversity(
        List<ChunkSearchHit> chunks,
        int maxChunksPerDoc,
        int maxTotalChunks)
    {
        var diverseChunks = new List<ChunkSearchHit>();
        var docCounts = new Dictionary<Guid, int>();

        foreach (var chunk in chunks)
        {
            if (!docCounts.TryGetValue(chunk.DocumentId, out var count))
            {
                count = 0;
            }

            if (count < maxChunksPerDoc)
            {
                diverseChunks.Add(chunk);
                docCounts[chunk.DocumentId] = count + 1;

                if (diverseChunks.Count >= maxTotalChunks)
                    break;
            }
        }

        return diverseChunks;
    }

    private class OllamaStreamChunk
    {
        [JsonPropertyName("response")] public string? Response { get; set; }
        [JsonPropertyName("done")]     public bool    Done     { get; set; }
    }
}

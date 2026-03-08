using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GED.Core.Interfaces;
using GED.Core.Models;

namespace GED.API.Controllers;

/// <summary>
/// Search controller.
///
/// Key design changes vs original:
///   1. /nlp/understand is REMOVED from the public API.
///      NLP understanding is now internal to /query — the frontend gets NLP metadata
///      (IsUnderstood, DetectedLanguage, NlpSummary) inside the SearchResult response.
///      This halves network round-trips and eliminates the frontend parallel-fetch pattern.
///
///   2. GET /suggestions?q= is ADDED (fixes the 404 observed in logs).
///      Returns lightweight autocomplete suggestions from recent NLP keyword extraction.
///      Never throws — returns an empty list on any error.
///
///   3. GET /suggestions/{documentId} (existing) is unchanged.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;
    private readonly INlpService    _nlpService;
    private readonly ILogger<SearchController> _logger;

    public SearchController(
        ISearchService searchService,
        INlpService nlpService,
        ILogger<SearchController> logger)
    {
        _searchService = searchService;
        _nlpService    = nlpService;
        _logger        = logger;
    }

    // ── POST /api/search/query ────────────────────────────────────────────────

    /// <summary>
    /// Multilingual hybrid search.
    ///
    /// The response includes:
    ///   - Documents (BM25 + semantic merged)
    ///   - IsUnderstood: false → frontend shows "Please enter a proper search term"
    ///   - DetectedLanguage: "en" | "fr" | "ar" | "unknown"
    ///   - NlpSummary: human-readable filter banner, e.g. "Factures · PDF · depuis 2024"
    ///   - SearchMode: BM25 | Semantic | Hybrid
    /// </summary>
    [HttpPost("query")]
    public async Task<ActionResult<SearchResult>> Search([FromBody] SearchRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "Request body is required." });

        try
        {
            var result = await _searchService.SearchAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing search for query '{Query}'", request.Query);
            return StatusCode(500, new { error = "Search failed", message = ex.Message });
        }
    }

    // ── GET /api/search/suggestions?q= ───────────────────────────────────────

    /// <summary>
    /// Autocomplete suggestions for the search bar.
    /// Uses local NLP keyword extraction — no LLM, &lt;5ms response.
    /// Returns an empty list (never 404/500) so the UI degrades gracefully.
    /// </summary>
    [HttpGet("suggestions")]
    public async Task<ActionResult<List<string>>> GetQuerySuggestions([FromQuery] string? q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(new List<string>());

        try
        {
            var nlQuery = await _nlpService.UnderstandQueryAsync(q);

            if (!nlQuery.IsUnderstood || !nlQuery.Keywords.Any())
                return Ok(new List<string>());

            // Return up to 5 keywords that could serve as search completions
            var suggestions = nlQuery.Keywords.Take(5).ToList();
            return Ok(suggestions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting suggestions for '{Q}' — returning empty list", q);
            return Ok(new List<string>());   // Never return 404 or 500 for autocomplete
        }
    }

    // ── GET /api/search/suggestions/{documentId} ──────────────────────────────

    /// <summary>
    /// Returns semantically similar documents for a given document.
    /// Uses kNN embedding similarity if available, falls back to MoreLikeThis.
    /// </summary>
    [HttpGet("suggestions/{documentId:guid}")]
    public async Task<ActionResult<List<DocumentSuggestion>>> GetDocumentSuggestions(
        Guid documentId, [FromQuery] int count = 5)
    {
        try
        {
            var suggestions = await _searchService.GetRelatedDocumentsAsync(documentId, count);
            return Ok(suggestions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting suggestions for document {Id}", documentId);
            return StatusCode(500, new { error = "Failed to get suggestions", message = ex.Message });
        }
    }

    // ── NOTE: POST /api/search/nlp/understand is intentionally removed. ────────
    // The frontend should read nlp data from the /query response fields:
    //   result.isUnderstood, result.detectedLanguage, result.nlpSummary
    // If you need to re-add it for debugging, you can expose it as:
    //
    //   [HttpPost("nlp/understand")]
    //   public async Task<IActionResult> UnderstandQuery([FromBody] string query)
    //       => Ok(await _nlpService.UnderstandQueryAsync(query));
}
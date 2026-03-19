using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Services;   // ← NEW: for AuthService
using System.Security.Claims;         // ← NEW: for ClaimTypes

namespace GED.API.Controllers;

/// <summary>
/// Search controller.
///
/// Injects the current user's identity (UserId, UserRole, AllowedCategories) into
/// every SearchRequest so OpenSearchService can enforce ACL filters at query time.
/// No JWT needed — identity comes from the session cookie set at login.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly ISearchService             _searchService;
    private readonly INlpService                _nlpService;
    private readonly AuthService                _authService;   // ← NEW
    private readonly ILogger<SearchController>  _logger;

    public SearchController(
        ISearchService            searchService,
        INlpService               nlpService,
        AuthService               authService,                 // ← NEW
        ILogger<SearchController> logger)
    {
        _searchService = searchService;
        _nlpService    = nlpService;
        _authService   = authService;                         // ← NEW
        _logger        = logger;
    }

    // ── POST /api/search/query ────────────────────────────────────────────────

    /// <summary>
    /// Multilingual hybrid search.
    /// User identity is read from the session cookie and injected into the request
    /// so the search layer can apply ACL filters transparently.
    /// </summary>
    [HttpPost("query")]
    public async Task<ActionResult<SearchResult>> Search([FromBody] SearchRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "Request body is required." });

        // ── NEW: populate user context from cookie claims ─────────────────────
        // Admin → no ACL filter injected (sees everything).
        // Others → OpenSearchService will add a filter based on UserId /
        //          AllowedCategories so only permitted documents are returned.
        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        var role     = User.FindFirst(ClaimTypes.Role)?.Value;

        if (!string.IsNullOrEmpty(username))
        {
            var user = _authService.GetUserByUsername(username);

            request.UserId                = user?.Id.ToString();
            request.UserRole              = role;
            request.UserAllowedCategories = user?.AllowedCategories;
        }

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
    /// Autocomplete suggestions — never throws, returns empty list on error.
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

            return Ok(nlQuery.Keywords.Take(5).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting suggestions for '{Q}' — returning empty list", q);
            return Ok(new List<string>());
        }
    }

    // ── GET /api/search/suggestions/{documentId} ──────────────────────────────

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
}
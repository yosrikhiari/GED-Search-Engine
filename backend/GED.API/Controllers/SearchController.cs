using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Services;
using GED.Infrastructure.Data;
using System.Security.Claims;

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
    private readonly AuthService                _authService;
    private readonly ILogger<SearchController>  _logger;

    public SearchController(
        ISearchService            searchService,
        INlpService               nlpService,
        AuthService               authService,
        ILogger<SearchController> logger)
    {
        _searchService = searchService;
        _nlpService    = nlpService;
        _authService   = authService;
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

        // Populate user context from cookie claims
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

    // ── POST /api/search/reindex ───────────────────────────────────────────────

    /// <summary>
    /// Manually triggers a full re-indexing of all documents.
    /// Admin only. This is an async operation.
    /// </summary>
    [HttpPost("reindex")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<object>> TriggerReindex()
    {
        try
        {
            _logger.LogInformation("Manual reindex triggered by admin");

            using var scope = HttpContext.RequestServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GED.Infrastructure.Data.GedDbContext>();
            var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();

            // Use raw SQL to bypass EF Core's JSON conversion issues
            var rawDocs = await db.Database
                .SqlQueryRaw<DocumentRow>(@"
                    SELECT id, title, description, file_name AS FileName, file_path AS FilePath, 
                           content_type AS ContentType, file_size AS FileSize, created_at AS CreatedAt, 
                           document_date AS DocumentDate, modified_at AS ModifiedAt, 
                           status AS Status, is_ocr_processed AS IsOcrProcessed, 
                           ocr_text AS OcrText, extracted_text AS ExtractedText, category AS Category 
                    FROM documents WHERE status = 'Indexed'")
                .ToListAsync();

            if (!rawDocs.Any())
            {
                return Ok(new { message = "No documents to re-index", count = 0 });
            }

            var domainDocs = new List<Document>();

            foreach (var e in rawDocs)
            {
                var tags = await GetTagsSafelyAsync(db, e.Id);
                var metadata = await GetMetadataSafelyAsync(db, e.Id);

                var doc = new Document
                {
                    Id            = e.Id,
                    Title         = e.Title,
                    Description   = e.Description,
                    FileName      = e.FileName,
                    FilePath      = e.FilePath,
                    ContentType   = e.ContentType,
                    FileSize      = e.FileSize,
                    CreatedAt     = e.CreatedAt,
                    DocumentDate  = e.DocumentDate,
                    ModifiedAt    = e.ModifiedAt,
                    Status        = Enum.TryParse<DocumentStatus>(e.Status, out var s) ? s : DocumentStatus.Indexed,
                    OcrText       = e.OcrText,
                    ExtractedText = e.ExtractedText,
                    Tags          = tags,
                    Category      = e.Category,
                    Metadata      = metadata,
                    IsOcrProcessed = e.IsOcrProcessed
                };
                domainDocs.Add(doc);
            }

            if (!domainDocs.Any())
            {
                return Ok(new { message = "No documents could be mapped for re-indexing", count = 0 });
            }

            await searchService.BulkIndexDocumentsAsync(domainDocs);

            _logger.LogInformation("Reindex completed: {Count} documents indexed", domainDocs.Count);

            return Ok(new { message = "Re-indexing completed", count = domainDocs.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual reindex");
            return StatusCode(500, new { error = "Reindex failed", message = ex.Message });
        }
    }

    private async Task<List<string>> GetTagsSafelyAsync(GED.Infrastructure.Data.GedDbContext db, Guid docId)
    {
        try
        {
            var tagsStr = await db.Database
                .SqlQueryRaw<string>("SELECT tags FROM documents WHERE id = {0}", docId)
                .FirstOrDefaultAsync();
            if (string.IsNullOrEmpty(tagsStr)) return new List<string>();
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(tagsStr) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private async Task<Dictionary<string, object>> GetMetadataSafelyAsync(GED.Infrastructure.Data.GedDbContext db, Guid docId)
    {
        try
        {
            var metaStr = await db.Database
                .SqlQueryRaw<string>("SELECT metadata FROM documents WHERE id = {0}", docId)
                .FirstOrDefaultAsync();
            if (string.IsNullOrEmpty(metaStr)) return new Dictionary<string, object>();
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(metaStr) ?? new Dictionary<string, object>();
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }

    private class DocumentRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long FileSize { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? DocumentDate { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public string Status { get; set; } = "";
        public bool IsOcrProcessed { get; set; }
        public string? OcrText { get; set; }
        public string? ExtractedText { get; set; }
        public string? Category { get; set; }
    }
}
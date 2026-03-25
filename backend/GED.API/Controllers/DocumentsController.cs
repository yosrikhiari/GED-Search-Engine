using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Data;
using GED.Infrastructure.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using static GED.Core.Models.OcrConstants;

namespace GED.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly ISearchService _searchService;
    private readonly IOcrService _ocrService;
    private readonly DocumentChunkingService _chunkingService;
    private readonly GedDbContext _db;
    private readonly ILogger<DocumentsController> _logger;
    private readonly IDistributedCache _cache;
    private readonly IConfiguration _configuration;
    private readonly AuthService _authService;
    private readonly IAuditService _auditService;

    private static readonly string[] AllowedCategories =
    {
        "Invoice", "Contract", "Report", "Letter",
        "Memo", "Presentation", "Spreadsheet", "Image", "Other"
    };

    public DocumentsController(
        IDocumentService documentService,
        ISearchService searchService,
        IOcrService ocrService,
        DocumentChunkingService chunkingService,
        GedDbContext db,
        ILogger<DocumentsController> logger,
        IConfiguration configuration,
        IDistributedCache cache,
        AuthService authService,
        IAuditService auditService)
    {
        _documentService = documentService;
        _searchService   = searchService;
        _ocrService      = ocrService;
        _chunkingService = chunkingService;
        _db              = db;
        _logger          = logger;
        _configuration   = configuration;
        _cache           = cache;
        _authService     = authService;
        _auditService    = auditService;
    }

[HttpPost("upload")]
public async Task<ActionResult<Document>> UploadDocument(
    IFormFile file,
    [FromForm] string? title    = null,
    [FromForm] string? category = null,
    [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null)
{
    if (!string.IsNullOrWhiteSpace(idempotencyKey))
    {
        var cacheKey = $"ged:upload:idem:{idempotencyKey}";
        var existing = await _cache.GetStringAsync(cacheKey);
        if (existing != null)
        {
            _logger.LogInformation(
                "Duplicate upload accepted — idempotency key {Key} already used",
                idempotencyKey);
            try
            {
                var cachedDoc = System.Text.Json.JsonSerializer.Deserialize<Document>(existing);
                if (cachedDoc != null && cachedDoc.Id != Guid.Empty)
                    return Ok(cachedDoc);
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "Corrupted idempotency cache for key {Key} — proceeding with new upload", idempotencyKey);
            }
        }
    }
    try
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        var maxSizeMb    = _configuration.GetValue<int>("Document:MaxUploadSizeMB", 100);
        var maxSizeBytes = (long)maxSizeMb * 1024 * 1024;

        if (file.Length > maxSizeBytes)
        {
            _logger.LogWarning(
                "File upload rejected — size {SizeMB:F1}MB exceeds limit of {LimitMB}MB",
                file.Length / 1_048_576.0, maxSizeMb);

            return BadRequest(new
            {
                error    = $"File size {file.Length / 1_048_576.0:F1}MB exceeds the maximum allowed size of {maxSizeMb}MB.",
                limitMb  = maxSizeMb,
                actualMb = Math.Round(file.Length / 1_048_576.0, 2)
            });
        }

        var allowedTypes = _configuration
            .GetSection("Document:AllowedFileTypes")
            .Get<string[]>() ?? Array.Empty<string>();

        if (allowedTypes.Length > 0 &&
            !allowedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                error        = $"File type '{file.ContentType}' is not allowed.",
                allowedTypes = allowedTypes
            });
        }

        if (string.IsNullOrWhiteSpace(category))
            return BadRequest(new { error = "Category is required. Please select a category for the document." });

        if (!AllowedCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new
            {
                error             = $"Invalid category '{category}'.",
                allowedCategories = AllowedCategories
            });

        // ── Category ACL enforcement ───────────────────────────────────────────
        // Admins can upload to any category; others are restricted by AllowedCategories
        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        var role     = User.FindFirst(ClaimTypes.Role)?.Value;

        if (role != "Admin" && !string.IsNullOrEmpty(username))
        {
            var user = _authService.GetUserByUsername(username);
            if (user?.AllowedCategories != null && user.AllowedCategories.Count > 0)
            {
                var normalizedCategory = AllowedCategories
                    .FirstOrDefault(c => c.Equals(category, StringComparison.OrdinalIgnoreCase)) ?? category;

                if (!user.AllowedCategories.Contains(normalizedCategory, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Category ACL violation — user '{User}' attempted to upload to category '{Category}' (allowed: [{Allowed}])",
                        username, category, string.Join(", ", user.AllowedCategories));

                    return StatusCode(403, new
                    {
                        error   = $"Access denied. You are not authorized to upload documents to the category '{category}'.",
                        allowed = user.AllowedCategories
                    });
                }
            }
        }

        // Sanitize filename to prevent path traversal attacks
        var sanitizedFileName = SanitizeFileName(file.FileName);
        if (sanitizedFileName != file.FileName)
        {
            _logger.LogWarning(
                "Filename sanitized: '{Original}' -> '{Sanitized}'",
                file.FileName, sanitizedFileName);
        }

        _logger.LogInformation(
            "Uploading document: {FileName} ({SizeMB:F2}MB), Title: {Title}, Category: {Category}",
            sanitizedFileName, file.Length / 1_048_576.0, title, category);

            using var stream = file.OpenReadStream();
            var metadata = new Dictionary<string, object>
            {
                ["category"] = category
            };

            var document = await _documentService.UploadDocumentAsync(
                stream, sanitizedFileName, file.ContentType,
                title ?? sanitizedFileName, metadata);

            _logger.LogInformation("Document uploaded with ID: {DocumentId}", document.Id);

            var indexed = await _searchService.IndexDocumentAsync(document);
            if (!indexed)
                _logger.LogWarning("Failed to index document {DocumentId}", document.Id);
            else
                _logger.LogInformation("Document {DocumentId} indexed successfully", document.Id);

            if (!string.IsNullOrWhiteSpace(document.ExtractedText))
            {
                var chunks = _chunkingService.ChunkText(document.Id, document.ExtractedText);
                if (chunks.Any())
                {
                    try
                    {
                        await _searchService.IndexChunksAsync(document, chunks);
                        _logger.LogInformation("Document {DocumentId} chunked into {Count} chunks for RAG", document.Id, chunks.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to index chunks for document {DocumentId}", document.Id);
                    }
                }
            }

                if (!string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    var cacheKey = $"ged:upload:idem:{idempotencyKey}";
                    await _cache.SetStringAsync(
                        cacheKey,
                        System.Text.Json.JsonSerializer.Serialize(document),
                        new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                        });
                }
                return CreatedAtAction(nameof(GetDocument), new { id = document.Id }, document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading document: {FileName}", file?.FileName);
            return StatusCode(500, new { error = "Upload failed", message = ex.Message });
        }
    }

    /// <summary>
    /// Batch upload multiple files with real-time SSE progress.
    /// Streams per-file status updates as Server-Sent Events.
    /// </summary>
    [HttpPost("upload/batch")]
    public async Task UploadBatchSse(CancellationToken cancellationToken)
    {
        var username = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        var role     = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        _logger.LogInformation("Batch upload started by {User} ({Role})", username, role);

        Response.Headers["Content-Type"]      = "text/event-stream";
        Response.Headers["Cache-Control"]      = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        async Task SendProgress(string eventType, object data)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(data);
                await Response.WriteAsync($"event: {eventType}\ndata: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
            catch { /* client disconnected */ }
        }

        try
        {
            var files = Request.Form.Files;
            var category = Request.Form["category"].FirstOrDefault();
            var titlesJson = Request.Form["titles"].FirstOrDefault();

            Dictionary<string, string>? titles = null;
            if (!string.IsNullOrEmpty(titlesJson))
            {
                try { titles = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(titlesJson); }
                catch { /* ignore malformed titles */ }
            }

            if (files.Count == 0)
            {
                await SendProgress("error", new { message = "No files provided" });
                return;
            }

            var totalFiles = files.Count;
            await SendProgress("start", new { total = totalFiles });

            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                var fileIndex = i;

                await SendProgress("file_start", new
                {
                    fileIndex,
                    fileName = file.FileName,
                    fileSize = file.Length,
                    progress = 0
                });

                try
                {
                    // Stage 1: Category ACL check
                    await SendProgress("file_progress", new { fileIndex, stage = "validating", progress = 10 });

                    if (!string.IsNullOrWhiteSpace(category))
                    {
                        if (!AllowedCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
                        {
                            await SendProgress("file_error", new
                            {
                                fileIndex,
                                fileName = file.FileName,
                                error = $"Invalid category '{category}'"
                            });
                            continue;
                        }

                        if (role != "Admin" && !string.IsNullOrEmpty(username))
                        {
                            var user = _authService.GetUserByUsername(username);
                            if (user?.AllowedCategories?.Count > 0 &&
                                !user.AllowedCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
                            {
                                await SendProgress("file_error", new
                                {
                                    fileIndex,
                                    fileName = file.FileName,
                                    error = $"Access denied to category '{category}'"
                                });
                                continue;
                            }
                        }
                    }

                    await SendProgress("file_progress", new { fileIndex, stage = "uploading", progress = 30 });

                    var sanitizedFileName = SanitizeFileName(file.FileName);
                    var title = titles?.GetValueOrDefault(file.FileName) ?? sanitizedFileName;

                    using var stream = file.OpenReadStream();
                    var metadata = new Dictionary<string, object> { ["category"] = category ?? "Other" };

                    await SendProgress("file_progress", new { fileIndex, stage = "processing", progress = 60 });

                    var document = await _documentService.UploadDocumentAsync(
                        stream, sanitizedFileName, file.ContentType, title, metadata, cancellationToken);

                    await SendProgress("file_progress", new { fileIndex, stage = "indexing", progress = 80 });

                    var indexed = await _searchService.IndexDocumentAsync(document);
                    if (!string.IsNullOrWhiteSpace(document.ExtractedText))
                    {
                        var chunks = _chunkingService.ChunkText(document.Id, document.ExtractedText);
                        if (chunks.Any())
                            await _searchService.IndexChunksAsync(document, chunks, cancellationToken);
                    }

                    await SendProgress("file_complete", new
                    {
                        fileIndex,
                        fileName = file.FileName,
                        documentId = document.Id,
                        title = document.Title,
                        category = document.Category,
                        fileSize = document.FileSize,
                        indexed,
                        progress = 100
                    });

                    _logger.LogInformation(
                        "Batch upload file {Index}/{Total}: {FileName} -> {DocId}",
                        i + 1, totalFiles, file.FileName, document.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Batch upload failed for file {FileName}", file.FileName);
                    await SendProgress("file_error", new
                    {
                        fileIndex,
                        fileName = file.FileName,
                        error = ex.Message
                    });
                }
            }

            await SendProgress("done", new { total = totalFiles });
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Batch upload cancelled by client");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch upload stream error");
            await SendProgress("error", new { message = ex.Message });
        }
        finally
        {
            try { Response.Body.Close(); } catch { /* ignore */ }
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Document>> GetDocument(Guid id)
    {
        try
        {
            var document = await _documentService.GetDocumentByIdAsync(id);
            return document == null ? NotFound() : Ok(document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document {DocumentId}", id);
            return StatusCode(500, new { error = "Failed to get document", message = ex.Message });
        }
    }

    [HttpGet("{id}/download")]
    public async Task<IActionResult> DownloadDocument(Guid id)
    {
        try
        {
            var document = await _documentService.GetDocumentByIdAsync(id);
            if (document == null) return NotFound();

            var stream = await _documentService.GetDocumentContentAsync(id);
            return File(stream, document.ContentType, document.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading document {DocumentId}", id);
            return StatusCode(500, new { error = "Download failed", message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDocument(Guid id)
    {
        try
        {
            var username = User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
            
            var marked = await _documentService.MarkDocumentAsDeletedAsync(id);
            
            var searchDeleted = await _searchService.DeleteDocumentIndexAsync(id);
            
            var deleted = await _documentService.DeleteDocumentAsync(id);
            
            if (!marked && !searchDeleted)
            {
                return NotFound(new { error = "Document not found", message = "Document does not exist in database or search index" });
            }
            
            // Audit logging
            _auditService.LogDocumentDelete(id, username, "User deleted document");
            
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document {DocumentId}", id);
            return StatusCode(500, new { error = "Delete failed", message = ex.Message });
        }
    }

    /// <summary>
    /// Bulk delete documents
    /// </summary>
    [HttpPost("bulk-delete")]
    public async Task<IActionResult> BulkDeleteDocuments([FromBody] List<Guid> documentIds)
    {
        if (documentIds == null || !documentIds.Any())
            return BadRequest(new { error = "No document IDs provided" });

        var username = User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
        
        // Fetch all documents in a single query to minimize DB round trips
        var existingDocs = await _db.Documents
            .Where(d => documentIds.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync();
        
        var foundIds = new HashSet<Guid>(existingDocs);
        var results = new List<dynamic>();
        var deletedCount = 0;

        // Process documents - parallel for search index deletion (independent)
        var searchDeleteTasks = documentIds
            .Where(id => foundIds.Contains(id))
            .Select(id => _searchService.DeleteDocumentIndexAsync(id));
        
        await Task.WhenAll(searchDeleteTasks);

        // Process each document
        foreach (var id in documentIds)
        {
            try
            {
                if (!foundIds.Contains(id))
                {
                    results.Add(new { id, success = false, error = "Document not found" });
                    continue;
                }

                var marked = await _documentService.MarkDocumentAsDeletedAsync(id);
                var deleted = await _documentService.DeleteDocumentAsync(id);
                
                if (marked || deleted)
                {
                    _auditService.LogDocumentDelete(id, username, "Bulk delete");
                    deletedCount++;
                    results.Add(new { id, success = true });
                }
                else
                {
                    results.Add(new { id, success = false, error = "Failed to delete" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error bulk deleting document {DocumentId}", id);
                results.Add(new { id, success = false, error = ex.Message });
            }
        }

        return Ok(new
        {
            total = documentIds.Count,
            deleted = deletedCount,
            failed = documentIds.Count - deletedCount,
            results
        });
    }

    /// <summary>
    /// Bulk update document category
    /// </summary>
    [HttpPost("bulk-update-category")]
    public async Task<IActionResult> BulkUpdateCategory([FromBody] BulkCategoryUpdateRequest request)
    {
        if (request.DocumentIds == null || !request.DocumentIds.Any())
            return BadRequest(new { error = "No document IDs provided" });

        if (string.IsNullOrWhiteSpace(request.Category))
            return BadRequest(new { error = "Category is required" });

        var username = User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
        var results = new List<dynamic>();
        var updatedCount = 0;

        foreach (var id in request.DocumentIds)
        {
            try
            {
                var doc = await _documentService.GetDocumentByIdAsync(id);
                if (doc == null)
                {
                    results.Add(new { id, success = false, error = "Document not found" });
                    continue;
                }

                doc.Category = request.Category;
                var updated = await _documentService.UpdateDocumentAsync(id, doc);
                await _searchService.UpdateDocumentIndexAsync(updated);
                
                _auditService.LogAudit(new AuditEvent
                {
                    EventType = "BULK_UPDATE",
                    PerformedBy = username,
                    TargetType = "Document",
                    TargetId = id.ToString(),
                    Action = $"Updated category to {request.Category}"
                });
                
                updatedCount++;
                results.Add(new { id, success = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error bulk updating document {DocumentId}", id);
                results.Add(new { id, success = false, error = ex.Message });
            }
        }

        return Ok(new
        {
            total = request.DocumentIds.Count,
            updated = updatedCount,
            failed = request.DocumentIds.Count - updatedCount,
            results
        });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Document>> UpdateDocument(Guid id, [FromBody] Document document)
    {
        try
        {
            if (id != document.Id)
                return BadRequest(new { error = "ID mismatch" });

            var updated = await _documentService.UpdateDocumentAsync(id, document);
            await _searchService.UpdateDocumentIndexAsync(updated);
            return Ok(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating document {DocumentId}", id);
            return StatusCode(500, new { error = "Update failed", message = ex.Message });
        }
    }

    /// <summary>
    /// Returns the current OCR pipeline stage for a document.
    ///
    /// Stage progression stored in document.Metadata (see OcrConstants.Stages):
    ///
    ///   (nothing)                      → Pending        (0)
    ///   ocr_stage = "processing"       → Processing     (1)
    ///   ocr_stage = "text_extracted"   → TextExtracted  (2)  ← Tesseract done
    ///   ocr_stage = "llm_cleaning"     → LlmCleaning    (3)  ← Ollama running
    ///   IsOcrProcessed = true          → Completed      (4)  ← full pipeline done
    ///   ocr_error present              → Failed         (5)
    ///
    /// IsOcrProcessed is set to true as soon as Tesseract finishes (stage
    /// text_extracted), so the frontend polling loop resolves quickly even
    /// while Ollama is still cleaning the text in the background.
    /// </summary>
    [HttpGet("{id}/ocr-status")]
    public async Task<ActionResult<OcrJob>> GetOcrStatus(Guid id)
    {
        try
        {
            var entity = await _db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);

            if (entity == null)
                return NotFound(new { error = $"Document {id} not found" });

            var ocrJob = new OcrJob
            {
                Id            = id,
                DocumentId    = id,
                CreatedAt     = entity.CreatedAt,
                CompletedAt   = entity.ModifiedAt,
                ExtractedText = entity.ExtractedText,
                RawTextLength = entity.OcrText?.Length ?? entity.ExtractedText?.Length ?? 0,
                IsOcrProcessed = entity.IsOcrProcessed,
            };

            // ── Resolve pipeline stage from metadata ─────────────────────────
            var meta  = entity.Metadata;
            var stage = GetMetaString(meta, OcrConstants.MetadataKeys.OcrStage);

            if (entity.Metadata != null && entity.Metadata.ContainsKey(OcrConstants.MetadataKeys.OcrError))
            {
                // Hard failure at any stage
                ocrJob.Status       = OcrStatus.Failed;
                ocrJob.ErrorMessage = GetMetaString(meta, OcrConstants.MetadataKeys.OcrError);
                ocrJob.StageLabel   = "Failed";
            }
            else if (entity.IsOcrProcessed && stage == OcrConstants.Stages.Completed)
            {
                // Full pipeline done
                ocrJob.Status     = OcrStatus.Completed;
                ocrJob.StageLabel = "Complete";
            }
            else if (entity.IsOcrProcessed && stage == OcrConstants.Stages.LlmCleaning)
            {
                // IsOcrProcessed was set after Tesseract, LLM still running
                ocrJob.Status     = OcrStatus.LlmCleaning;
                ocrJob.StageLabel = "Enhancing with AI…";
            }
            else if (entity.IsOcrProcessed && stage == OcrConstants.Stages.TextExtracted)
            {
                // Tesseract finished, LLM hasn't started yet
                ocrJob.Status     = OcrStatus.TextExtracted;
                ocrJob.StageLabel = "Text extracted, queued for AI cleaning";
            }
            else if (entity.IsOcrProcessed)
            {
                // IsOcrProcessed true but no ocr_stage key — legacy docs or
                // the native-text shortcut path: treat as completed.
                ocrJob.Status     = OcrStatus.Completed;
                ocrJob.StageLabel = "Complete";
            }
            else if (stage == "processing")
            {
                ocrJob.Status     = OcrStatus.Processing;
                ocrJob.StageLabel = "Reading document…";
            }
            else
            {
                // Default: waiting in queue
                ocrJob.Status     = OcrStatus.Pending;
                ocrJob.StageLabel = "Queued";
            }

            // Confidence if stored
            if (meta != null &&
                meta.TryGetValue("ocr_confidence", out var conf) &&
                conf is System.Text.Json.JsonElement je)
            {
                ocrJob.Confidence = (float)je.GetDouble();
            }

            _logger.LogInformation(
                "OCR status for {DocumentId}: {Status} ({Stage}), IsOcrProcessed={IsOcrProcessed}, RawLen={RawLen}",
                id, ocrJob.Status, ocrJob.StageLabel, entity.IsOcrProcessed, ocrJob.RawTextLength);

            ocrJob.Tags         = entity.Tags;
            ocrJob.Description  = entity.Description;
            ocrJob.DocumentDate = entity.DocumentDate;
            ocrJob.Category     = entity.Category;
            ocrJob.ModifiedAt   = entity.ModifiedAt;

            return Ok(ocrJob);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting OCR status for document {DocumentId}", id);
            return StatusCode(500, new { error = "Failed to get OCR status", message = ex.Message });
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static string? GetMetaString(Dictionary<string, object>? meta, string key)
    {
        if (meta == null || !meta.TryGetValue(key, out var val)) return null;

        return val switch
        {
            string s => s,
            System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.String
                => je.GetString(),
            _ => val?.ToString()
        };
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "unnamed";

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName
            .Where(c => !invalid.Contains(c))
            .ToArray());

        // Prevent directory traversal
        sanitized = sanitized.Replace("..", "").Replace("~", "");

        // Limit length
        if (sanitized.Length > 200)
            sanitized = sanitized[..200];

        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
    }

    /// <summary>
    /// Export documents to CSV format with pagination support
    /// </summary>
    [HttpPost("export")]
    public async Task<IActionResult> ExportDocuments(
        [FromBody] List<Guid>? documentIds = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 1000)
    {
        try
        {
            // Enforce max page size to prevent memory issues
            pageSize = Math.Min(pageSize, 5000);
            page = Math.Max(page, 1);

            List<Document> documents;
            int totalCount;

            if (documentIds != null && documentIds.Any())
            {
                // Export specific documents (respect pagination)
                var skip = (page - 1) * pageSize;
                var pagedIds = documentIds.Skip(skip).Take(pageSize).ToList();
                documents = await _documentService.GetDocumentsByIdsAsync(pagedIds);
                totalCount = documentIds.Count;
            }
            else
            {
                // Export all accessible documents with pagination
                var query = _db.Documents
                    .Where(d => d.Status != DocumentStatus.Deleted);
                
                totalCount = await query.CountAsync();
                
                documents = await query
                    .OrderByDescending(d => d.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync()
                    .ContinueWith(t => t.Result.Select(MapToDocumentDto).ToList());
            }

            // Generate CSV
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Id,Title,Description,FileName,ContentType,FileSize,Category,Tags,CreatedAt,DocumentDate,Status");

            foreach (var doc in documents)
            {
                var tags = doc.Tags != null ? string.Join(";", doc.Tags) : "";
                csv.AppendLine($"\"{doc.Id}\",\"{EscapeCsv(doc.Title)}\",\"{EscapeCsv(doc.Description ?? "")}\",\"{EscapeCsv(doc.FileName)}\",\"{doc.ContentType}\",{doc.FileSize},\"{doc.Category}\",\"{EscapeCsv(tags)}\",\"{doc.CreatedAt:yyyy-MM-dd HH:mm:ss}\",\"{doc.DocumentDate:yyyy-MM-dd}\",\"{doc.Status}\"");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
            
            // Add pagination info to response headers
            Response.Headers["X-Total-Count"] = totalCount.ToString();
            Response.Headers["X-Page"] = page.ToString();
            Response.Headers["X-Page-Size"] = pageSize.ToString();
            
            return File(bytes, "text/csv", $"ged-export-{DateTime.UtcNow:yyyyMMdd}-page{page}.csv");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting documents");
            return StatusCode(500, new { error = "Export failed", message = ex.Message });
        }
    }

    /// <summary>
    /// Retry OCR processing for stuck documents
    /// </summary>
    [HttpPost("retry-ocr")]
    public async Task<IActionResult> RetryOcr([FromBody] List<Guid>? documentIds = null)
    {
        try
        {
            var username = User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
            
            List<DocumentEntity> documents;
            
            if (documentIds != null && documentIds.Any())
            {
                documents = await _db.Documents
                    .Where(d => documentIds.Contains(d.Id))
                    .ToListAsync();
            }
            else
            {
                var needsEnrichment = await _db.Documents
                    .Where(d => d.IsOcrProcessed && 
                                d.ExtractedText != null && 
                                d.ExtractedText.Length >= 100 &&
                                (d.Metadata == null || !d.Metadata.ContainsKey("extracted_date")))
                    .Take(20)
                    .ToListAsync();

                var allDocs = await _db.Documents
                    .Where(d => !d.IsOcrProcessed || d.ExtractedText == null || d.ExtractedText.Length < 100)
                    .Take(50)
                    .ToListAsync();
                
                var withErrors = allDocs.Where(d => d.Metadata != null && d.Metadata.ContainsKey("ocr_error")).ToList();
                var pendingOnly = allDocs.Where(d => d.Metadata == null || !d.Metadata.ContainsKey("ocr_error")).ToList();
                
                documents = needsEnrichment.Concat(withErrors).Concat(pendingOnly).Take(20).ToList();
            }
            
            var retriedCount = 0;
            var results = new List<dynamic>();
            
            foreach (var doc in documents)
            {
                var outboxMessage = new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    Type = "OcrJob",
                    Payload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        JobId = Guid.NewGuid(),
                        DocumentId = doc.Id,
                        Language = "eng+fra+ara"
                    }),
                    CreatedAt = DateTime.UtcNow,
                    RetryCount = 0
                };
                _db.OutboxMessages.Add(outboxMessage);
                
                doc.Metadata ??= new Dictionary<string, object>();
                doc.Metadata["ocr_retry_requested"] = DateTime.UtcNow.ToString("o");
                doc.Metadata["ocr_retry_requested_by"] = username;
                
                results.Add(new { id = doc.Id, success = true });
                retriedCount++;
            }
            
            await _db.SaveChangesAsync();
            
            _logger.LogInformation("OCR retry queued for {Count} documents by {User}", retriedCount, username);
            
            return Ok(new { 
                retriedCount, 
                message = retriedCount > 0 
                    ? $"Queued {retriedCount} OCR jobs for retry" 
                    : "No stuck documents found to retry",
                results 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrying OCR");
            return StatusCode(500, new { error = "Retry failed", message = ex.Message });
        }
    }

    private static string EscapeCsv(string value)
    {
        return value?.Replace("\"", "\"\"") ?? "";
    }

    private static Document MapToDocumentDto(DocumentEntity e) => new()
    {
        Id               = e.Id,
        Title            = e.Title,
        Description      = e.Description,
        FileName         = e.FileName,
        ContentType      = e.ContentType,
        FileSize         = e.FileSize,
        FileHash         = e.FileHash,
        CreatedAt        = e.CreatedAt,
        DocumentDate     = e.DocumentDate,
        ModifiedAt       = e.ModifiedAt,
        CreatedBy        = e.CreatedBy,
        Status           = e.Status,
        IsOcrProcessed   = e.IsOcrProcessed,
        OcrText          = e.OcrText,
        ExtractedText    = e.ExtractedText,
        Tags             = e.Tags,
        Category         = e.Category,
        Version          = e.Version
    };
}
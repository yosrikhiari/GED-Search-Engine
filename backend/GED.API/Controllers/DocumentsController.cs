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
    private readonly IWebhookService _webhookService;

    private static readonly string[] AllowedCategories =
    {
        "Invoice", "Contract", "Report", "Letter",
        "Memo", "Presentation", "Spreadsheet", "Image", "Other"
    };

    private static readonly string[] AllowedMimeTypes =
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/tiff",
        "text/plain", "text/csv",
        "audio/mpeg", "audio/wav", "video/mp4"
    };

    private const long MaxFileSizeBytes = 500 * 1024 * 1024; // 500 MB

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
        IWebhookService webhookService)
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
        _webhookService  = webhookService;
    }

    private static readonly string[] AdminOnlyCategories = { };

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

        if (!AllowedMimeTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Upload rejected — MIME type '{MimeType}' not in AllowedMimeTypes list",
                file.ContentType);
            return BadRequest(new
            {
                error = $"File type '{file.ContentType}' is not supported.",
                allowedTypes = AllowedMimeTypes
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

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _webhookService.TriggerEventAsync("document.created", new WebhookPayload
                        {
                            Event = "document.created",
                            Document = new WebhookDocumentData
                            {
                                Id = document.Id,
                                Title = document.Title,
                                Category = document.Category,
                                FileName = document.FileName,
                                ContentType = document.ContentType,
                                FileSize = document.FileSize,
                                CreatedBy = document.CreatedBy,
                                CreatedAt = document.CreatedAt
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Webhook delivery failed for document.created: {DocId}", document.Id);
                    }
                });

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

    [HttpGet]
    public async Task<ActionResult<List<Document>>> GetAllDocuments([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        try
        {
            var documents = await _db.Documents
                .OrderByDescending(d => d.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            return Ok(documents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all documents");
            return StatusCode(500, new { error = "Failed to get documents", message = ex.Message });
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
            var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
            var deleted = await _documentService.DeleteDocumentAsync(id);
            if (!deleted) return NotFound();

            await _searchService.DeleteDocumentIndexAsync(id);

            _ = Task.Run(async () =>
            {
                try
                {
                    await _webhookService.TriggerEventAsync("document.deleted", new WebhookPayload
                    {
                        Event = "document.deleted",
                        Document = doc != null ? new WebhookDocumentData
                        {
                            Id = doc.Id,
                            Title = doc.Title,
                            Category = doc.Category,
                            FileName = doc.FileName,
                            ContentType = doc.ContentType,
                            FileSize = doc.FileSize,
                            CreatedBy = doc.CreatedBy,
                            CreatedAt = doc.CreatedAt
                        } : null
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Webhook delivery failed for document.deleted: {DocId}", id);
                }
            });

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document {DocumentId}", id);
            return StatusCode(500, new { error = "Delete failed", message = ex.Message });
        }
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

            _ = Task.Run(async () =>
            {
                try
                {
                    await _webhookService.TriggerEventAsync("document.updated", new WebhookPayload
                    {
                        Event = "document.updated",
                        Document = new WebhookDocumentData
                        {
                            Id = updated.Id,
                            Title = updated.Title,
                            Category = updated.Category,
                            FileName = updated.FileName,
                            ContentType = updated.ContentType,
                            FileSize = updated.FileSize,
                            CreatedBy = updated.CreatedBy,
                            CreatedAt = updated.CreatedAt
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Webhook delivery failed for document.updated: {DocId}", updated.Id);
                }
            });

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
    /// Stage progression stored in document.Metadata:
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
            var stage = GetMetaString(meta, "ocr_stage");

            if (entity.Metadata != null && entity.Metadata.ContainsKey("ocr_error"))
            {
                // Hard failure at any stage
                ocrJob.Status       = OcrStatus.Failed;
                ocrJob.ErrorMessage = GetMetaString(meta, "ocr_error");
                ocrJob.StageLabel   = "Failed";
            }
            else if (entity.IsOcrProcessed && stage == "completed")
            {
                // Full pipeline done
                ocrJob.Status     = OcrStatus.Completed;
                ocrJob.StageLabel = "Complete";
            }
            else if (entity.IsOcrProcessed && stage == "llm_cleaning")
            {
                // IsOcrProcessed was set after Tesseract, LLM still running
                ocrJob.Status     = OcrStatus.LlmCleaning;
                ocrJob.StageLabel = "Enhancing with AI…";
            }
            else if (entity.IsOcrProcessed && stage == "text_extracted")
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
}
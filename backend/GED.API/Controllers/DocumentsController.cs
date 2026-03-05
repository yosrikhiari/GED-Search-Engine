using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Data;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;

namespace GED.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly ISearchService _searchService;
    private readonly IOcrService _ocrService;
    private readonly GedDbContext _db;
    private readonly ILogger<DocumentsController> _logger;
    private readonly IDistributedCache _cache;
    private readonly IConfiguration _configuration;

    private static readonly string[] AllowedCategories =
    {
        "Invoice", "Contract", "Report", "Letter",
        "Memo", "Presentation", "Spreadsheet", "Image", "Other"
    };

    public DocumentsController(
        IDocumentService documentService,
        ISearchService searchService,
        IOcrService ocrService,
        GedDbContext db,
        ILogger<DocumentsController> logger,
        IConfiguration configuration,
        IDistributedCache cache)
    {
        _documentService = documentService;
        _searchService   = searchService;
        _ocrService      = ocrService;
        _db              = db;
        _logger          = logger;
        _configuration   = configuration;
        _cache           = cache;
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
                "Duplicate upload rejected — idempotency key {Key} already used", 
                idempotencyKey);
            return Ok(System.Text.Json.JsonSerializer
                .Deserialize<Document>(existing));
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

            _logger.LogInformation(
                "Uploading document: {FileName} ({SizeMB:F2}MB), Title: {Title}, Category: {Category}",
                file.FileName, file.Length / 1_048_576.0, title, category);

            using var stream = file.OpenReadStream();
            var metadata = new Dictionary<string, object>
            {
                ["category"] = category
            };

            var document = await _documentService.UploadDocumentAsync(
                stream, file.FileName, file.ContentType,
                title ?? file.FileName, metadata);

            _logger.LogInformation("Document uploaded with ID: {DocumentId}", document.Id);

            var indexed = await _searchService.IndexDocumentAsync(document);
            if (!indexed)
                _logger.LogWarning("Failed to index document {DocumentId}", document.Id);
            else
                _logger.LogInformation("Document {DocumentId} indexed successfully", document.Id);

            // Queue OCR for images and PDFs
            // Outbox Pattern: write OCR job to DB in the same operation as the document.
            // OutboxRelayService will publish to RabbitMQ when it's available.
            // This prevents silent OCR loss if RabbitMQ is temporarily down during upload.
            if (document.ContentType.StartsWith("image/") || document.ContentType == "application/pdf")
            {
                var outboxMsg = new OutboxMessage
                {
                    Type    = "OcrJob",
                    Payload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        JobId      = Guid.NewGuid(),
                        DocumentId = document.Id,
                        Language   = "eng+fra+ara"   // match your existing OCR languages
                    })
                };
                _db.OutboxMessages.Add(outboxMsg);
                await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "📥 OCR job queued via outbox for document {DocumentId}", document.Id);
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
            var deleted = await _documentService.DeleteDocumentAsync(id);
            if (!deleted) return NotFound();

            await _searchService.DeleteDocumentIndexAsync(id);
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
}
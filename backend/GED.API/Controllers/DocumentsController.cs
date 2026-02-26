using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Data;
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
    private readonly IConfiguration _configuration;

    // Allowed categories — single source of truth
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
        IConfiguration configuration)
    {
        _documentService = documentService;
        _searchService   = searchService;
        _ocrService      = ocrService;
        _db              = db;
        _logger          = logger;
        _configuration   = configuration;
    }

    [HttpPost("upload")]
    public async Task<ActionResult<Document>> UploadDocument(
        IFormFile file,
        [FromForm] string? title    = null,
        [FromForm] string? category = null)
    {
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
            if (document.ContentType.StartsWith("image/") ||
                document.ContentType == "application/pdf")
            {
                await _ocrService.QueueOcrJobAsync(document.Id);
                _logger.LogInformation("OCR job queued for document {DocumentId}", document.Id);
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

    // ── FIX: Query the database directly instead of the broken in-memory cache ──
    //
    // The original bug:
    //   - TesseractOcrService._jobCache is keyed by job.Id (a new Guid per job)
    //   - But this endpoint receives the documentId, not the jobId
    //   - OcrWorkerService runs in its own DI scope → its TesseractOcrService instance
    //     has a SEPARATE _jobCache that is never visible to the controller
    //   - Result: always 404, even after OCR completes
    //
    // The fix:
    //   - Inject GedDbContext into the controller (it's already registered in DI)
    //   - Query documents.is_ocr_processed directly — this is updated by OcrWorkerService
    //   - Synthesize an OcrJob response from the DB row so the API contract stays the same
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

            // Synthesize an OcrJob from the document's current DB state.
            // This is always accurate because OcrWorkerService writes IsOcrProcessed
            // and ExtractedText directly to the documents table.
            var ocrJob = new OcrJob
            {
                Id           = id,            // reuse documentId as a stable identifier
                DocumentId   = id,
                CreatedAt    = entity.CreatedAt,
                CompletedAt  = entity.ModifiedAt,
                ExtractedText = entity.ExtractedText,
            };

            // Determine status from DB columns
            if (entity.IsOcrProcessed)
            {
                ocrJob.Status = OcrStatus.Completed;
            }
            else if (entity.Metadata != null &&
                     entity.Metadata.ContainsKey("ocr_error"))
            {
                ocrJob.Status       = OcrStatus.Failed;
                ocrJob.ErrorMessage = entity.Metadata["ocr_error"]?.ToString();
            }
            else if (entity.Metadata != null &&
                     entity.Metadata.ContainsKey("ocr_processed_at"))
            {
                // Worker set ocr_empty=true (no text found) but still marked processed
                ocrJob.Status = OcrStatus.Completed;
            }
            else
            {
                // Not yet processed — still pending or in-flight
                ocrJob.Status = OcrStatus.Pending;
            }

            // Include OCR confidence if available
            if (entity.Metadata != null &&
                entity.Metadata.TryGetValue("ocr_confidence", out var conf) &&
                conf is System.Text.Json.JsonElement je)
            {
                ocrJob.Confidence = (float)je.GetDouble();
            }

            _logger.LogInformation(
                "OCR status for document {DocumentId}: {Status}, IsOcrProcessed={IsOcrProcessed}",
                id, ocrJob.Status, entity.IsOcrProcessed);

            return Ok(ocrJob);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting OCR status for document {DocumentId}", id);
            return StatusCode(500, new { error = "Failed to get OCR status", message = ex.Message });
        }
    }
}
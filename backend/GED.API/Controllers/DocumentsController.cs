using Microsoft.AspNetCore.Mvc;
using GED.Core.Interfaces;
using GED.Core.Models;

namespace GED.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly ISearchService _searchService;
    private readonly IOcrService _ocrService;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentService documentService,
        ISearchService searchService,
        IOcrService ocrService,
        ILogger<DocumentsController> logger)
    {
        _documentService = documentService;
        _searchService = searchService;
        _ocrService = ocrService;
        _logger = logger;
    }

    [HttpPost("upload")]
    public async Task<ActionResult<Document>> UploadDocument(IFormFile file, [FromForm] string? title = null, [FromForm] string? category = null)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file uploaded" });
            }

            _logger.LogInformation("Uploading document: {FileName}, Title: {Title}, Category: {Category}", 
                file.FileName, title, category);

            using var stream = file.OpenReadStream();
            var metadata = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(category))
            {
                metadata["category"] = category;
            }

            var document = await _documentService.UploadDocumentAsync(
                stream,
                file.FileName,
                file.ContentType,
                title ?? file.FileName,
                metadata
            );

            _logger.LogInformation("Document uploaded with ID: {DocumentId}", document.Id);

            // Index the document - CRITICAL for search
            var indexed = await _searchService.IndexDocumentAsync(document);
            if (!indexed)
            {
                _logger.LogWarning("Failed to index document {DocumentId}", document.Id);
            }
            else
            {
                _logger.LogInformation("Document {DocumentId} successfully indexed", document.Id);
            }

            // Queue OCR if it's an image or PDF
            if (document.ContentType.StartsWith("image/") || document.ContentType == "application/pdf")
            {
                await _ocrService.QueueOcrJobAsync(document.Id);
                _logger.LogInformation("OCR job queued for document {DocumentId}", document.Id);
            }

            return CreatedAtAction(nameof(GetDocument), new { id = document.Id }, document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading document: {FileName}", file?.FileName);
            return StatusCode(500, new { error = "Upload failed", message = ex.Message, details = ex.ToString() });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Document>> GetDocument(Guid id)
    {
        try
        {
            var document = await _documentService.GetDocumentByIdAsync(id);
            if (document == null)
            {
                return NotFound();
            }
            return Ok(document);
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
            if (document == null)
            {
                return NotFound();
            }

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
            if (!deleted)
            {
                return NotFound();
            }

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
            {
                return BadRequest(new { error = "ID mismatch" });
            }

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

    [HttpGet("{id}/ocr-status")]
    public async Task<ActionResult<OcrJob>> GetOcrStatus(Guid id)
    {
        try
        {
            var job = await _ocrService.GetOcrJobStatusAsync(id);
            if (job == null)
            {
                return NotFound();
            }
            return Ok(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting OCR status");
            return StatusCode(500, new { error = "Failed to get OCR status", message = ex.Message });
        }
    }
}
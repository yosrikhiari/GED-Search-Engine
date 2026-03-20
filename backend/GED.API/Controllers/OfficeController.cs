using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GED.Infrastructure.Services;
using GED.Core.Interfaces;

namespace GED.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OfficeController : ControllerBase
{
    private readonly IOfficeOnlineService _officeService;
    private readonly IDocumentService _documentService;
    private readonly ILogger<OfficeController> _logger;

    public OfficeController(
        IOfficeOnlineService officeService,
        IDocumentService documentService,
        ILogger<OfficeController> logger)
    {
        _officeService = officeService;
        _documentService = documentService;
        _logger = logger;
    }

    /// <summary>
    /// Get Office Online viewer URL for a document.
    /// Requires the document to have a publicly accessible URL.
    /// </summary>
    [HttpGet("{documentId:guid}/viewer-url")]
    public async Task<IActionResult> GetViewerUrl(Guid documentId)
    {
        var doc = await _documentService.GetDocumentByIdAsync(documentId);
        if (doc == null) return NotFound(new { error = "Document not found." });

        if (!_officeService.IsSupported(doc.ContentType))
        {
            return Ok(new
            {
                supported = false,
                contentType = doc.ContentType,
                message = $"'{doc.ContentType}' is not supported by Microsoft Office Online. " +
                          "Use the built-in text extraction preview instead."
            });
        }

        // In production, this would be the CDN/public URL of the document
        var publicUrl = GeneratePublicUrl(doc.Id, doc.FileName);
        var viewerUrl = _officeService.GenerateViewerUrl(publicUrl, doc.ContentType);

        return Ok(new
        {
            supported = true,
            documentId = doc.Id,
            contentType = doc.ContentType,
            viewerUrl,
            publicUrl,
            title = doc.Title
        });
    }

    /// <summary>
    /// Generate a shareable, time-limited Office Online link.
    /// </summary>
    [HttpPost("{documentId:guid}/share")]
    public async Task<IActionResult> GenerateShareableUrl(
        Guid documentId,
        [FromBody] ShareUrlRequest? request)
    {
        var doc = await _documentService.GetDocumentByIdAsync(documentId);
        if (doc == null) return NotFound(new { error = "Document not found." });

        var validFor = request?.ValidForHours switch
        {
            > 0 and <= 720 => TimeSpan.FromHours(request.ValidForHours),
            _ => TimeSpan.FromHours(24)
        };

        var publicUrl = GeneratePublicUrl(doc.Id, doc.FileName);
        var result = _officeService.GenerateShareableUrl(publicUrl, doc.ContentType, doc.Title, validFor);

        if (!result.Success)
            return BadRequest(new { error = result.Error });

        _logger.LogInformation(
            "Generated Office Online shareable URL for document {DocId} ({Title})",
            documentId, doc.Title);

        return Ok(result);
    }

    /// <summary>
    /// Check if a content type is supported by Office Online.
    /// </summary>
    [HttpGet("supported-types")]
    public IActionResult GetSupportedTypes()
    {
        return Ok(new[]
        {
            new { type = "application/pdf",                          extension = "pdf",  name = "PDF" },
            new { type = "application/msword",                       extension = "doc",  name = "Word 97-2003" },
            new { type = "application/vnd.openxmlformats-officedocument.wordprocessingml.document", extension = "docx", name = "Word (OOXML)" },
            new { type = "application/vnd.ms-excel",                 extension = "xls",  name = "Excel 97-2003" },
            new { type = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", extension = "xlsx", name = "Excel (OOXML)" },
            new { type = "application/vnd.ms-powerpoint",             extension = "ppt",  name = "PowerPoint 97-2003" },
            new { type = "application/vnd.openxmlformats-officedocument.presentationml.presentation", extension = "pptx", name = "PowerPoint (OOXML)" }
        });
    }

    private static string GeneratePublicUrl(Guid documentId, string fileName)
    {
        // In production, this would be a CDN URL or Azure Blob Storage SAS URL
        // For now, return an internal API URL that the viewer can fetch
        return $"http://localhost:5001/api/documents/{documentId}/download";
    }
}

public class ShareUrlRequest
{
    public int ValidForHours { get; set; } = 24;
}

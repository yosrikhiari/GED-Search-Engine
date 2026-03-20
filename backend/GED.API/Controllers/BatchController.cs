using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GED.Core.Interfaces;
using System.IO.Compression;
using System.Security.Claims;

namespace GED.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BatchController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly ISearchService _searchService;
    private readonly ILogger<BatchController> _logger;
    private const int MaxBatchDownloadSize = 50;

    public BatchController(
        IDocumentService documentService,
        ISearchService searchService,
        ILogger<BatchController> logger)
    {
        _documentService = documentService;
        _searchService = searchService;
        _logger = logger;
    }

    /// <summary>
    /// Download multiple documents as a ZIP archive.
    /// </summary>
    [HttpPost("download")]
    public async Task<IActionResult> DownloadZip(
        [FromBody] BatchDownloadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DocumentIds == null || request.DocumentIds.Count == 0)
            return BadRequest(new { error = "No document IDs provided." });

        if (request.DocumentIds.Count > MaxBatchDownloadSize)
            return BadRequest(new { error = $"Maximum {MaxBatchDownloadSize} documents per batch download." });

        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        _logger.LogInformation(
            "Batch download requested by {User}: {Count} documents",
            username, request.DocumentIds.Count);

        var documents = await _documentService.GetDocumentsByIdsAsync(
            request.DocumentIds.Distinct(), cancellationToken);

        if (!documents.Any())
            return NotFound(new { error = "No documents found matching the provided IDs." });

        var archiveName = $"ged-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
        Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{archiveName}\"");

        // Build ZIP in memory
        using var memoryStream = new MemoryStream();
        using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var doc in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (request.IncludeOriginalFiles)
                    {
                        var content = await _documentService.GetDocumentContentAsync(doc.Id, cancellationToken);
                        var entry = zipArchive.CreateEntry(
                            SanitizeEntryName(doc.FileName),
                            CompressionLevel.Optimal);
                        await using var entryStream = entry.Open();
                        await content.CopyToAsync(entryStream, cancellationToken);
                    }

                    if (request.IncludeOcrText && !string.IsNullOrWhiteSpace(doc.OcrText))
                    {
                        var textEntry = zipArchive.CreateEntry(
                            SanitizeEntryName(doc.FileName) + ".txt",
                            CompressionLevel.Optimal);
                        await using var ts = textEntry.Open();
                        await using var tw = new StreamWriter(ts);
                        await tw.WriteAsync(doc.OcrText);
                    }

                    if (request.IncludeMetadata)
                    {
                        var metadata = new
                        {
                            doc.Id,
                            doc.Title,
                            doc.Description,
                            doc.FileName,
                            doc.ContentType,
                            doc.FileSize,
                            doc.Category,
                            doc.DocumentDate,
                            doc.CreatedAt,
                            doc.Tags,
                            Status = doc.Status.ToString(),
                            OcrProcessed = doc.IsOcrProcessed
                        };

                        var json = System.Text.Json.JsonSerializer.Serialize(metadata,
                            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                        var metaEntry = zipArchive.CreateEntry(
                            SanitizeEntryName(doc.FileName) + ".meta.json",
                            CompressionLevel.Optimal);
                        await using var ms = metaEntry.Open();
                        await using var mw = new StreamWriter(ms);
                        await mw.WriteAsync(json);
                    }

                    _logger.LogInformation("Added doc {Id} ({Title}) to ZIP", doc.Id, doc.Title);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add document {Id} to ZIP", doc.Id);
                }
            }
        }

        _logger.LogInformation(
            "Batch download: {Count} docs, archive: {Archive}",
            documents.Count, archiveName);

        Response.ContentType = "application/zip";
        memoryStream.Position = 0;
        await memoryStream.CopyToAsync(Response.Body, cancellationToken);

        return new EmptyResult();
    }

    /// <summary>
    /// Get download estimate (size, count) before creating ZIP.
    /// </summary>
    [HttpPost("download/estimate")]
    public async Task<ActionResult<BatchDownloadEstimate>> GetDownloadEstimate(
        [FromBody] BatchDownloadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DocumentIds == null || !request.DocumentIds.Any())
            return BadRequest(new { error = "No document IDs provided." });

        var documents = await _documentService.GetDocumentsByIdsAsync(
            request.DocumentIds.Distinct(), cancellationToken);

        long estimatedBytes = 0;
        foreach (var doc in documents)
        {
            estimatedBytes += doc.FileSize;
            if (request.IncludeOcrText && !string.IsNullOrWhiteSpace(doc.OcrText))
                estimatedBytes += doc.OcrText.Length;
        }

        return Ok(new BatchDownloadEstimate
        {
            DocumentCount = documents.Count,
            FoundCount = documents.Count,
            EstimatedSizeBytes = estimatedBytes,
            EstimatedSizeFormatted = FormatBytes(estimatedBytes)
        });
    }

    private static string SanitizeEntryName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "document";

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName
            .Where(c => !invalid.Contains(c))
            .ToArray());

        // Prevent zip slip attack
        while (sanitized.StartsWith(".") || sanitized.Contains(".."))
            sanitized = sanitized.TrimStart('.').Replace("..", "");

        return string.IsNullOrWhiteSpace(sanitized) ? "document" : sanitized;
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int i = 0;
        double size = bytes;
        while (size >= 1024 && i < suffixes.Length - 1)
        { size /= 1024; i++; }
        return $"{size:F1} {suffixes[i]}";
    }
}

public class BatchDownloadRequest
{
    public List<Guid> DocumentIds { get; set; } = new();
    public bool IncludeOriginalFiles { get; set; } = true;
    public bool IncludeOcrText { get; set; } = true;
    public bool IncludeMetadata { get; set; } = true;
}

public class BatchDownloadEstimate
{
    public int DocumentCount { get; set; }
    public int FoundCount { get; set; }
    public long EstimatedSizeBytes { get; set; }
    public string EstimatedSizeFormatted { get; set; } = "0 B";
}

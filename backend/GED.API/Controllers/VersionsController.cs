using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GED.Infrastructure.Services;

namespace GED.API.Controllers;

[ApiController]
[Route("api/documents/{documentId}/versions")]
[Authorize]
public class VersionsController : ControllerBase
{
    private readonly IVersionHistoryService _versionService;
    private readonly ILogger<VersionsController> _logger;

    public VersionsController(
        IVersionHistoryService versionService,
        ILogger<VersionsController> logger)
    {
        _versionService = versionService;
        _logger = logger;
    }

    /// <summary>
    /// Get version history for a document.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetHistory(Guid documentId)
    {
        var history = await _versionService.GetVersionHistoryAsync(documentId);
        return Ok(history);
    }

    /// <summary>
    /// Get a specific version of a document.
    /// </summary>
    [HttpGet("{versionNumber:int}")]
    public async Task<IActionResult> GetVersion(Guid documentId, int versionNumber)
    {
        var version = await _versionService.GetVersionAsync(documentId, versionNumber);
        return version == null ? NotFound() : Ok(version);
    }

    /// <summary>
    /// Restore a document to a previous version.
    /// </summary>
    [HttpPost("{versionNumber:int}/restore")]
    public async Task<IActionResult> RestoreVersion(
        Guid documentId,
        int versionNumber,
        [FromBody] RestoreVersionRequest? request)
    {
        if (versionNumber <= 0)
            return BadRequest(new { error = "Invalid version number." });

        var username = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "system";
        var restored = await _versionService.RestoreVersionAsync(
            documentId, versionNumber, request?.RestoredBy ?? username);

        if (restored == null)
            return NotFound(new { error = "Document or version not found." });

        _logger.LogInformation(
            "Document {DocId} restored to version {Version} by {User}",
            documentId, versionNumber, username);

        return Ok(new
        {
            message = $"Document restored to version {versionNumber}",
            document = restored
        });
    }

    /// <summary>
    /// Get version comparison (diff) between two versions.
    /// </summary>
    [HttpGet("compare")]
    public async Task<IActionResult> CompareVersions(
        Guid documentId,
        [FromQuery] int v1,
        [FromQuery] int v2)
    {
        if (v1 <= 0 || v2 <= 0)
            return BadRequest(new { error = "Version numbers must be positive." });

        var version1 = await _versionService.GetVersionAsync(documentId, v1);
        var version2 = await _versionService.GetVersionAsync(documentId, v2);

        if (version1 == null || version2 == null)
            return NotFound(new { error = "One or both versions not found." });

        var changes = new List<string>();

        if (version1.Title != version2.Title)
            changes.Add($"Title changed: \"{version1.Title}\" → \"{version2.Title}\"");

        if (version1.Description != version2.Description)
            changes.Add("Description modified");

        if (version1.Category != version2.Category)
            changes.Add($"Category changed: \"{version1.Category}\" → \"{version2.Category}\"");

        if (version1.FileName != version2.FileName)
            changes.Add($"Filename changed: \"{version1.FileName}\" → \"{version2.FileName}\"");

        return Ok(new
        {
            version1 = version1.VersionNumber,
            version2 = version2.VersionNumber,
            changes,
            changeCount = changes.Count
        });
    }
}

public class RestoreVersionRequest
{
    public string? RestoredBy { get; set; }
}

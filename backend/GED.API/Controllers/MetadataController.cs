using Microsoft.AspNetCore.Mvc;
using GED.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace GED.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MetadataController : ControllerBase
{
    private readonly DocumentMetadataService _metadataService;
    private readonly ILogger<MetadataController> _logger;

    public MetadataController(
        DocumentMetadataService metadataService,
        ILogger<MetadataController> logger)
    {
        _metadataService = metadataService;
        _logger = logger;
    }

    [HttpPost("suggest")]
    public async Task<ActionResult<DocumentMetadataSuggestion>> SuggestMetadata(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("No file provided to metadata suggest endpoint");
                return BadRequest(new { error = "No file provided" });
            }

            _logger.LogInformation("Suggesting metadata for file: {FileName}, Size: {FileSize}, Type: {ContentType}", 
                file.FileName, file.Length, file.ContentType);

            using var stream = file.OpenReadStream();
            
            var suggestion = await _metadataService.SuggestMetadataAsync(
                stream,
                file.FileName,
                file.ContentType
            );

            _logger.LogInformation("Metadata suggestion generated: Title={Title}, Category={Category}, Confidence={Confidence}", 
                suggestion.Title, suggestion.Category, suggestion.Confidence);

            return Ok(suggestion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error suggesting metadata for file: {FileName}", file?.FileName);
            
            // Return a fallback suggestion even on error
            var fallbackSuggestion = new DocumentMetadataSuggestion
            {
                Title = file?.FileName ?? "Untitled Document",
                Category = "Other",
                Confidence = 0.3f
            };
            
            _logger.LogInformation("Returning fallback suggestion: {Suggestion}", fallbackSuggestion);
            return Ok(fallbackSuggestion);
        }
    }
}
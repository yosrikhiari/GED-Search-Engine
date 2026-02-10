using Microsoft.AspNetCore.Mvc;
using GED.Core.Interfaces;
using GED.Core.Models;

namespace GED.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;
    private readonly ILogger<SearchController> _logger;

    public SearchController(ISearchService searchService, ILogger<SearchController> logger)
    {
        _searchService = searchService;
        _logger = logger;
    }

    [HttpPost("query")]
    public async Task<ActionResult<SearchResult>> Search([FromBody] SearchRequest request)
    {
        try
        {
            var result = await _searchService.SearchAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing search");
            return StatusCode(500, new { error = "Search failed", message = ex.Message });
        }
    }

    [HttpGet("suggestions/{documentId}")]
    public async Task<ActionResult<List<DocumentSuggestion>>> GetSuggestions(Guid documentId, [FromQuery] int count = 5)
    {
        try
        {
            var suggestions = await _searchService.GetRelatedDocumentsAsync(documentId, count);
            return Ok(suggestions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting suggestions for {DocumentId}", documentId);
            return StatusCode(500, new { error = "Failed to get suggestions", message = ex.Message });
        }
    }

    [HttpPost("nlp/understand")]
    public async Task<ActionResult<NaturalLanguageQuery>> UnderstandQuery([FromBody] string query)
    {
        try
        {
            var result = await _searchService.ProcessNaturalLanguageQueryAsync(query);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing NLP query");
            return StatusCode(500, new { error = "NLP processing failed", message = ex.Message });
        }
    }
}

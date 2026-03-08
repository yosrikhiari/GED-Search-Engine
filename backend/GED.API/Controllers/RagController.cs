using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Services;
using System.Security.Claims;

namespace GED.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]                          // ← was missing
public class RagController : ControllerBase
{
    private readonly IRagService _ragService;
    private readonly AuthService _authService;   // ← added
    private readonly ILogger<RagController> _logger;

    public RagController(
        IRagService ragService,
        AuthService authService,     // ← added
        ILogger<RagController> logger)
    {
        _ragService   = ragService;
        _authService  = authService;
        _logger       = logger;
    }

    [HttpPost("ask")]
    [ProducesResponseType(typeof(RagResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RagResponse>> Ask([FromBody] RagRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request?.Query))
                return BadRequest(new { error = "Query is required." });

            // Stamp the authenticated user's identity — cannot be spoofed by the client
            request.Username = User.FindFirst(ClaimTypes.Name)?.Value;

            _logger.LogInformation(
                "RAG request: query='{Query}', lang='{Lang}', categories={Categories}, user='{User}'",
                request.Query, request.Language,
                request.Categories != null ? string.Join(",", request.Categories) : "none",
                request.Username ?? "anonymous");

            var result = await _ragService.AskAsync(request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing RAG request");
            return StatusCode(500, new { error = "RAG processing failed", message = ex.Message });
        }
    }

    [HttpGet("health")]
    [AllowAnonymous]                 // health check doesn't need auth
    public IActionResult Health()
    {
        return Ok(new
        {
            status    = "healthy",
            module    = "RAG",
            timestamp = DateTime.UtcNow
        });
    }
}
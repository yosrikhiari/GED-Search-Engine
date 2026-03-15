using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Services;
using System.Security.Claims;
using System.Text.Json;

namespace GED.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RagController : ControllerBase
{
    private readonly IRagService            _ragService;
    private readonly AuthService            _authService;
    private readonly ILogger<RagController> _logger;

    public RagController(
        IRagService            ragService,
        AuthService            authService,
        ILogger<RagController> logger)
    {
        _ragService  = ragService;
        _authService = authService;
        _logger      = logger;
    }

    // ── POST /api/rag/ask (non-streaming, kept for backward compatibility) ────

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

    // ── POST /api/rag/ask/stream (SSE streaming) ──────────────────────────────

    [HttpPost("ask/stream")]
    public async Task AskStream([FromBody] RagRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Query))
        {
            Response.StatusCode = 400;
            return;
        }

        request.Username = User.FindFirst(ClaimTypes.Name)?.Value;

        _logger.LogInformation(
            "RAG stream request: query='{Query}', lang='{Lang}', user='{User}'",
            request.Query, request.Language, request.Username ?? "anonymous");

        Response.Headers["Content-Type"]      = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var token in _ragService.AskStreamAsync(request, cancellationToken))
            {
                var data = JsonSerializer.Serialize(new { token });
                await Response.WriteAsync($"data: {data}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing RAG stream request");
            var error = JsonSerializer.Serialize(new { error = "RAG processing failed" });
            await Response.WriteAsync($"data: {error}\n\n", cancellationToken);
        }
    }

    // ── GET /api/rag/health ───────────────────────────────────────────────────

    [HttpGet("health")]
    [AllowAnonymous]
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
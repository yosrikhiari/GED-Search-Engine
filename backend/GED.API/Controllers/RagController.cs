using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Services;
using System.Security.Claims;
using System.Text.Json;

namespace GED.API.Controllers;

/// <summary>
/// RAG controller.
/// Injects the current user's identity (UserId, UserRole, AllowedCategories) into
/// every RagRequest so the search layer can enforce ACL filters on the context
/// retrieval step, exactly as it does for regular searches.
/// </summary>
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

    // ── Helper: populate user identity on a RagRequest ────────────────────────

    /// <summary>
    /// Resolves the current user's full profile from the session cookie claims
    /// and stores UserId / UserRole / UserAllowedCategories on the request.
    /// These fields are [JsonIgnore] so they never come from the client body.
    /// </summary>
    private void InjectUserContext(RagRequest request)
    {
        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        var role     = User.FindFirst(ClaimTypes.Role)?.Value;

        request.Username = username;
        request.UserRole = role;

        if (!string.IsNullOrEmpty(username))
        {
            var user = _authService.GetUserByUsername(username);

            request.UserId                = user?.Id.ToString();
            request.UserAllowedCategories = user?.AllowedCategories;
        }
    }

    // ── POST /api/rag/ask ─────────────────────────────────────────────────────

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

            // ── NEW ──────────────────────────────────────────────────────────
            InjectUserContext(request);

            _logger.LogInformation(
                "RAG request: query='{Query}', lang='{Lang}', categories={Categories}, user='{User}', role='{Role}'",
                request.Query, request.Language,
                request.Categories != null ? string.Join(",", request.Categories) : "none",
                request.Username ?? "anonymous",
                request.UserRole ?? "unknown");

            var result = await _ragService.AskAsync(request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing RAG request");
            return StatusCode(500, new { error = "RAG processing failed", message = ex.Message });
        }
    }

    // ── POST /api/rag/ask/stream ──────────────────────────────────────────────

    private const int StreamTimeoutSeconds = 300; // 5 minutes max

    [HttpPost("ask/stream")]
    public async Task AskStream([FromBody] RagRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Query))
        {
            Response.StatusCode = 400;
            return;
        }

        // ── NEW ──────────────────────────────────────────────────────────────
        InjectUserContext(request);

        _logger.LogInformation(
            "RAG stream request: query='{Query}', lang='{Lang}', user='{User}', role='{Role}'",
            request.Query, request.Language,
            request.Username ?? "anonymous",
            request.UserRole ?? "unknown");

        Response.Headers["Content-Type"]      = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        // Create linked cancellation token with timeout to prevent memory leaks
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(StreamTimeoutSeconds));

        try
        {
            await foreach (var token in _ragService.AskStreamAsync(request, timeoutCts.Token))
            {
                var data = JsonSerializer.Serialize(new { token });
                await Response.WriteAsync($"data: {data}\n\n", timeoutCts.Token);
                await Response.Body.FlushAsync(timeoutCts.Token);
            }

            await Response.WriteAsync("data: {\"done\":true}\n\n", CancellationToken.None);
            await Response.Body.FlushAsync(CancellationToken.None);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("RAG stream timed out after {Seconds}s", StreamTimeoutSeconds);
            try
            {
                var timeoutMsg = JsonSerializer.Serialize(new { error = "Stream timed out", timedOut = true });
                await Response.WriteAsync($"data: {timeoutMsg}\n\n", CancellationToken.None);
                await Response.Body.FlushAsync(CancellationToken.None);
            }
            catch { /* client disconnected */ }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Error processing RAG stream request");
            try
            {
                var error = JsonSerializer.Serialize(new { error = "RAG processing failed" });
                await Response.WriteAsync($"data: {error}\n\n", CancellationToken.None);
                await Response.Body.FlushAsync(CancellationToken.None);
            }
            catch { /* client disconnected */ }
        }
        finally
        {
            try { Response.Body.Close(); } catch { /* ignore */ }
        }
    }

    // ── GET /api/rag/health ───────────────────────────────────────────────────

    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health()
        => Ok(new { status = "healthy", module = "RAG", timestamp = DateTime.UtcNow });
}
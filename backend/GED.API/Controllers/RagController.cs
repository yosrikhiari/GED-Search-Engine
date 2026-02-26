using Microsoft.AspNetCore.Mvc;
using GED.Core.Interfaces;
using GED.Core.Models;

namespace GED.API.Controllers;

/// <summary>
/// RAG (Retrieval Augmented Generation) controller.
///
/// Exposes the AI question-answering endpoint that:
///   1. Retrieves relevant documents from OpenSearch
///   2. Generates a synthetic answer using Ollama (local LLM)
///   3. Returns the answer with source documents and excerpts
///
/// This satisfies the "Module IA - RAG" requirement from the cahier des charges.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class RagController : ControllerBase
{
    private readonly IRagService _ragService;
    private readonly ILogger<RagController> _logger;

    public RagController(IRagService ragService, ILogger<RagController> logger)
    {
        _ragService = ragService;
        _logger     = logger;
    }

    /// <summary>
    /// Ask a natural-language question about your document base.
    /// Returns an AI-generated answer with source documents.
    /// </summary>
    /// <remarks>
    /// Example request:
    ///   POST /api/rag/ask
    ///   { "query": "Trouve les factures du projet X en 2024", "language": "fr" }
    /// </remarks>
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

            _logger.LogInformation(
                "RAG request: query='{Query}', lang='{Lang}', categories={Categories}",
                request.Query, request.Language,
                request.Categories != null ? string.Join(",", request.Categories) : "none");

            var result = await _ragService.AskAsync(request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing RAG request");
            return StatusCode(500, new { error = "RAG processing failed", message = ex.Message });
        }
    }

    /// <summary>
    /// Health check for the RAG module specifically.
    /// </summary>
    [HttpGet("health")]
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

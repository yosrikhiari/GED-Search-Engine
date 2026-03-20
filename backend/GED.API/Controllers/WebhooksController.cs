using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GED.Core.Models;
using GED.Infrastructure.Services;

namespace GED.API.Controllers;

[ApiController]
[Route("api/webhooks")]
[Authorize(Roles = "Admin")]
public class WebhooksController : ControllerBase
{
    private readonly IWebhookService _webhookService;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(IWebhookService webhookService, ILogger<WebhooksController> logger)
    {
        _webhookService = webhookService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<WebhookConfig>>> GetAll()
        => Ok(await _webhookService.GetAllWebhooksAsync());

    [HttpGet("{id}")]
    public async Task<ActionResult<WebhookConfig>> Get(Guid id)
    {
        var webhook = await _webhookService.GetWebhookByIdAsync(id);
        return webhook == null ? NotFound() : Ok(webhook);
    }

    [HttpPost]
    public async Task<ActionResult<WebhookConfig>> Create([FromBody] WebhookConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Name) || string.IsNullOrWhiteSpace(config.Url))
            return BadRequest(new { error = "Name and URL are required." });

        if (!Uri.TryCreate(config.Url, UriKind.Absolute, out _))
            return BadRequest(new { error = "Invalid webhook URL." });

        var created = await _webhookService.CreateWebhookAsync(config);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<WebhookConfig>> Update(Guid id, [FromBody] WebhookConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Name) || string.IsNullOrWhiteSpace(config.Url))
            return BadRequest(new { error = "Name and URL are required." });

        var updated = await _webhookService.UpdateWebhookAsync(id, config);
        return updated == null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _webhookService.DeleteWebhookAsync(id);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("{id}/test")]
    public async Task<IActionResult> Test(Guid id)
    {
        var webhook = await _webhookService.GetWebhookByIdAsync(id);
        if (webhook == null) return NotFound();

        try
        {
            await _webhookService.TriggerEventAsync("test", new
            {
                message = "This is a test webhook from GED Search Engine",
                triggeredAt = DateTime.UtcNow
            });

            return Ok(new { message = "Test event triggered successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Test failed", message = ex.Message });
        }
    }

    [HttpGet("events")]
    public IActionResult GetAvailableEvents()
    {
        return Ok(new[]
        {
            new { Event = "document.created",       Description = "Fired when a new document is uploaded" },
            new { Event = "document.updated",       Description = "Fired when a document is modified" },
            new { Event = "document.deleted",       Description = "Fired when a document is deleted" },
            new { Event = "document.access_granted", Description = "Fired when access is granted to a document" },
            new { Event = "document.access_revoked", Description = "Fired when access is revoked from a document" }
        });
    }
}

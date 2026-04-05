using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using GED.Infrastructure.Data;
using GED.API.Models;

namespace GED.API.Controllers;

[ApiController]
[Route("api/webhooks")]
[Authorize(Roles = "Admin")]
public class WebhookDeliveriesController : ControllerBase
{
    private readonly GedDbContext _db;
    private readonly ILogger<WebhookDeliveriesController> _logger;

    public WebhookDeliveriesController(GedDbContext db, ILogger<WebhookDeliveriesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("deliveries")]
    public async Task<ActionResult<WebhookDeliveryListResult>> GetDeliveries(
        [FromQuery] string? eventName = null,
        [FromQuery] bool? succeeded = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.WebhookDeliveries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(eventName))
            query = query.Where(d => d.Event == eventName);

        if (succeeded.HasValue)
            query = query.Where(d => d.Succeeded == succeeded.Value);

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new WebhookDeliveryDto
            {
                Id = d.Id,
                WebhookConfigId = d.WebhookConfigId,
                Event = d.Event,
                ResponseStatusCode = d.ResponseStatusCode,
                Succeeded = d.Succeeded,
                ErrorMessage = d.ErrorMessage,
                DurationMs = d.DurationMs,
                AttemptNumber = d.AttemptNumber,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync();

        return Ok(new WebhookDeliveryListResult
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("deliveries/{id:guid}")]
    public async Task<ActionResult<WebhookDeliveryDto>> GetDelivery(Guid id)
    {
        var d = await _db.WebhookDeliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (d == null) return NotFound(ErrorResponse.Create("Webhook delivery not found"));

        return Ok(new WebhookDeliveryDto
        {
            Id = d.Id,
            WebhookConfigId = d.WebhookConfigId,
            Event = d.Event,
            Payload = d.Payload,
            ResponseStatusCode = d.ResponseStatusCode,
            ResponseBody = d.ResponseBody,
            Succeeded = d.Succeeded,
            ErrorMessage = d.ErrorMessage,
            DurationMs = d.DurationMs,
            AttemptNumber = d.AttemptNumber,
            CreatedAt = d.CreatedAt
        });
    }

    [HttpGet("stats")]
    public async Task<ActionResult<WebhookStats>> GetStats()
    {
        var total = await _db.WebhookDeliveries.CountAsync();
        var succeeded = await _db.WebhookDeliveries.CountAsync(d => d.Succeeded);
        var failed = total - succeeded;

        var last24h = await _db.WebhookDeliveries
            .Where(d => d.CreatedAt >= DateTime.UtcNow.AddHours(-24))
            .GroupBy(d => d.Event)
            .Select(g => new EventStats
            {
                Event = g.Key,
                Count = g.Count(),
                SuccessRate = Math.Round(g.Average(d => d.Succeeded ? 1.0 : 0.0) * 100, 2)
            })
            .ToListAsync();

        return Ok(new WebhookStats
        {
            TotalDeliveries = total,
            Succeeded = succeeded,
            Failed = failed,
            SuccessRate = total > 0 ? Math.Round((double)succeeded / total * 100, 2) : 0,
            Last24HoursByEvent = last24h
        });
    }
}

public class WebhookDeliveryDto
{
    public Guid Id { get; set; }
    public Guid? WebhookConfigId { get; set; }
    public string Event { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public int AttemptNumber { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WebhookDeliveryListResult
{
    public List<WebhookDeliveryDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class WebhookStats
{
    public int TotalDeliveries { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public double SuccessRate { get; set; }
    public List<EventStats> Last24HoursByEvent { get; set; } = new();
}

public class EventStats
{
    public string Event { get; set; } = string.Empty;
    public int Count { get; set; }
    public double SuccessRate { get; set; }
}

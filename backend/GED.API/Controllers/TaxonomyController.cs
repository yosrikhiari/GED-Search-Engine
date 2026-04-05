using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using GED.API.Models;

namespace GED.API.Controllers;

[ApiController]
[Route("api/taxonomy")]
[Authorize(Roles = "Admin")]
public class TaxonomyController : ControllerBase
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<TaxonomyController> _logger;
    private readonly string _taxonomyFilePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private List<TaxonomyCategory> _categories = new();
    private List<TaxonomyTag> _tags = new();
    private readonly object _lock = new();

    public TaxonomyController(
        IDistributedCache cache,
        ILogger<TaxonomyController> logger,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _cache = cache;
        _logger = logger;
        _taxonomyFilePath = configuration["Taxonomy:FilePath"] ?? "/var/lib/ged/taxonomy.json";
        LoadTaxonomy();
    }

    // ── Categories ────────────────────────────────────────────────────────────

    [HttpGet("categories")]
    public Task<IActionResult> GetCategories()
    {
        lock (_lock) return Task.FromResult<IActionResult>(Ok(_categories.ToList()));
    }

    [HttpPost("categories")]
    public async Task<ActionResult<TaxonomyCategory>> CreateCategory([FromBody] CreateCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ErrorResponse.Create("Category name is required."));

        TaxonomyCategory category;
        lock (_lock)
        {
            if (_categories.Any(c => c.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase)))
                return BadRequest(ErrorResponse.Create($"Category '{request.Name}' already exists."));

            category = new TaxonomyCategory
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Description = request.Description,
                Icon = request.Icon ?? "📁",
                Color = request.Color ?? "#6366f1",
                SortOrder = _categories.Count > 0 ? _categories.Max(c => c.SortOrder) + 1 : 0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _categories.Add(category);
        }

        await SaveTaxonomyAsync();
        _logger.LogInformation("Created taxonomy category: {Name}", category.Name);
        return CreatedAtAction(nameof(GetCategories), category);
    }

    [HttpPut("categories/{id:guid}")]
    public async Task<ActionResult<TaxonomyCategory>> UpdateCategory(Guid id, [FromBody] UpdateCategoryRequest request)
    {
        TaxonomyCategory? category;
        lock (_lock)
        {
            category = _categories.FirstOrDefault(c => c.Id == id);
            if (category == null) return NotFound(ErrorResponse.Create("Category not found"));

            if (!string.IsNullOrWhiteSpace(request.Name) && request.Name != category.Name)
            {
                if (_categories.Any(c => c.Id != id && c.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase)))
                    return BadRequest(ErrorResponse.Create($"Category '{request.Name}' already exists."));
                category.Name = request.Name.Trim();
            }

            if (request.Description != null) category.Description = request.Description;
            if (request.Icon != null) category.Icon = request.Icon;
            if (request.Color != null) category.Color = request.Color;
            if (request.SortOrder.HasValue) category.SortOrder = request.SortOrder.Value;
            if (request.IsActive.HasValue) category.IsActive = request.IsActive.Value;

            category.UpdatedAt = DateTime.UtcNow;
        }

        await SaveTaxonomyAsync();
        return Ok(category);
    }

    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id)
    {
        lock (_lock)
        {
            var removed = _categories.RemoveAll(c => c.Id == id);
            if (removed == 0) return NotFound(ErrorResponse.Create("Category not found or already deleted"));
        }
        await SaveTaxonomyAsync();
        _logger.LogInformation("Deleted taxonomy category {Id}", id);
        return NoContent();
    }

    [HttpPost("categories/reorder")]
    public async Task<IActionResult> ReorderCategories([FromBody] List<Guid> categoryIds)
    {
        lock (_lock)
        {
            for (int i = 0; i < categoryIds.Count; i++)
            {
                var cat = _categories.FirstOrDefault(c => c.Id == categoryIds[i]);
                if (cat != null) cat.SortOrder = i;
            }
        }
        await SaveTaxonomyAsync();
        return Ok(_categories.OrderBy(c => c.SortOrder).ToList());
    }

    // ── Tags ─────────────────────────────────────────────────────────────────

    [HttpGet("tags")]
    public async Task<ActionResult<TaxonomyTagList>> GetTags(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var allTags = _tags.ToList();

        if (!string.IsNullOrWhiteSpace(search))
            allTags = allTags.Where(t =>
                t.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (t.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            ).ToList();

        if (!string.IsNullOrWhiteSpace(category))
            allTags = allTags.Where(t => t.Category == category).ToList();

        var total = allTags.Count;
        var paged = allTags
            .OrderBy(t => t.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new TaxonomyTagList
        {
            Tags = paged,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpGet("tags/{id:guid}")]
    public IActionResult GetTag(Guid id)
    {
        lock (_lock)
        {
            var tag = _tags.FirstOrDefault(t => t.Id == id);
            if (tag == null)
                return NotFound(ErrorResponse.Create("Taxonomy entry not found", HttpContext.Items["CorrelationId"]?.ToString()));
            return Ok(tag);
        }
    }

    [HttpPost("tags")]
    public async Task<ActionResult<TaxonomyTag>> CreateTag([FromBody] CreateTagRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ErrorResponse.Create("Tag name is required."));

        var normalizedName = NormalizeTagName(request.Name);
        TaxonomyTag tag;
        lock (_lock)
        {
            if (_tags.Any(t => t.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)))
                return BadRequest(ErrorResponse.Create($"Tag '{request.Name}' already exists."));

            tag = new TaxonomyTag
            {
                Id = Guid.NewGuid(),
                Name = normalizedName,
                Description = request.Description,
                Category = request.Category,
                Color = request.Color ?? "#64748b",
                UsageCount = 0,
                IsSystem = false,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _tags.Add(tag);
        }

        await SaveTaxonomyAsync();
        _logger.LogInformation("Created taxonomy tag: {Name}", tag.Name);
        return CreatedAtAction(nameof(GetTags), new { id = tag.Id }, tag);
    }

    [HttpPut("tags/{id:guid}")]
    public async Task<ActionResult<TaxonomyTag>> UpdateTag(Guid id, [FromBody] UpdateTagRequest request)
    {
        TaxonomyTag? tag;
        lock (_lock)
        {
            tag = _tags.FirstOrDefault(t => t.Id == id);
            if (tag == null) return NotFound(ErrorResponse.Create("Tag not found"));
            if (tag.IsSystem) return BadRequest(ErrorResponse.Create("System tags cannot be modified."));

            if (!string.IsNullOrWhiteSpace(request.Name) && request.Name != tag.Name)
            {
                var normalized = NormalizeTagName(request.Name);
                if (_tags.Any(t => t.Id != id && t.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                    return BadRequest(ErrorResponse.Create($"Tag '{request.Name}' already exists."));
                tag.Name = normalized;
            }

            if (request.Description != null) tag.Description = request.Description;
            if (request.Category != null) tag.Category = request.Category;
            if (request.Color != null) tag.Color = request.Color;
            if (request.IsActive.HasValue) tag.IsActive = request.IsActive.Value;
            tag.UpdatedAt = DateTime.UtcNow;
        }

        await SaveTaxonomyAsync();
        return Ok(tag);
    }

    [HttpDelete("tags/{id:guid}")]
    public async Task<IActionResult> DeleteTag(Guid id)
    {
        lock (_lock)
        {
            var tag = _tags.FirstOrDefault(t => t.Id == id);
            if (tag == null) return NotFound(ErrorResponse.Create("Tag not found"));
            if (tag.IsSystem) return BadRequest(ErrorResponse.Create("System tags cannot be deleted."));

            _tags.Remove(tag);
        }
        await SaveTaxonomyAsync();
        return NoContent();
    }

    [HttpPost("tags/bulk")]
    public async Task<ActionResult<List<TaxonomyTag>>> CreateBulkTags([FromBody] CreateBulkTagsRequest request)
    {
        if (request.Names == null || !request.Names.Any())
            return BadRequest(ErrorResponse.Create("Tag names are required."));

        if (request.Names.Count > 100)
            return BadRequest(ErrorResponse.Create("Maximum 100 tags per request."));

        var created = new List<TaxonomyTag>();

        lock (_lock)
        {
            foreach (var name in request.Names.Distinct())
            {
                var normalized = NormalizeTagName(name);
                if (string.IsNullOrWhiteSpace(normalized)) continue;
                if (_tags.Any(t => t.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase))) continue;

                _tags.Add(new TaxonomyTag
                {
                    Id = Guid.NewGuid(),
                    Name = normalized,
                    Description = request.Description,
                    Category = request.Category,
                    Color = request.Color ?? "#64748b",
                    UsageCount = 0,
                    IsSystem = false,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await SaveTaxonomyAsync();
        _logger.LogInformation("Created bulk tags: {Count}", request.Names.Count);
        return Ok(_tags.Where(t => created.Select(c => c.Id).Contains(t.Id)).ToList());
    }

    /// <summary>
    /// Increment usage count for tags (called when documents are tagged).
    /// </summary>
    [HttpPost("tags/track-usage")]
    public async Task<IActionResult> TrackTagUsage([FromBody] TrackTagUsageRequest request)
    {
        if (request.TagNames == null || !request.TagNames.Any()) return Ok();

        lock (_lock)
        {
            foreach (var name in request.TagNames)
            {
                var normalized = NormalizeTagName(name);
                var tag = _tags.FirstOrDefault(t => t.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
                if (tag != null) tag.UsageCount++;
            }
        }
        await SaveTaxonomyAsync();
        return Ok();
    }

    /// <summary>
    /// Get tag suggestions based on partial input.
    /// </summary>
    [HttpGet("tags/suggest")]
    public IActionResult SuggestTags([FromQuery] string q, [FromQuery] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(Enumerable.Empty<string>());

        List<TaxonomyTag> results;
        lock (_lock)
        {
            results = _tags
                .Where(t => t.IsActive && t.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.UsageCount)
                .Take(limit)
                .ToList();
        }

        return Ok(results.Select(t => t.Name).ToList());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task SaveTaxonomyAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_taxonomyFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var data = new TaxonomyData
            {
                Categories = _categories,
                Tags = _tags,
                UpdatedAt = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(data, _jsonOptions);
            await System.IO.File.WriteAllTextAsync(_taxonomyFilePath, json);

            // Also update Redis cache
            await _cache.SetStringAsync("ged:taxonomy", json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save taxonomy to {Path}", _taxonomyFilePath);
        }
    }

    private void LoadTaxonomy()
    {
        try
        {
            var cachedTaxonomy = _cache.GetString("ged:taxonomy");
            TaxonomyData? data = null;

            if (!string.IsNullOrEmpty(cachedTaxonomy))
            {
                data = JsonSerializer.Deserialize<TaxonomyData>(cachedTaxonomy, _jsonOptions);
            }
            else if (System.IO.File.Exists(_taxonomyFilePath))
            {
                var json = System.IO.File.ReadAllText(_taxonomyFilePath);
                data = JsonSerializer.Deserialize<TaxonomyData>(json, _jsonOptions);
            }

            if (data != null)
            {
                _categories = data.Categories ?? new();
                _tags = data.Tags ?? new();
            }
            else
            {
                // Seed default taxonomy
                _categories = GetDefaultCategories();
                _tags = GetDefaultTags();
            }

            _logger.LogInformation(
                "Loaded taxonomy: {CatCount} categories, {TagCount} tags",
                _categories.Count, _tags.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load taxonomy — using defaults");
            _categories = GetDefaultCategories();
            _tags = GetDefaultTags();
        }
    }

    private static string NormalizeTagName(string name)
        => name.Trim().ToLowerInvariant().Replace(" ", "-").Replace("#", "");

    private static List<TaxonomyCategory> GetDefaultCategories() => new()
    {
        new() { Id = Guid.NewGuid(), Name = "Invoice", Description = "Factures et devis", Icon = "📄", Color = "#3b82f6", SortOrder = 0, IsActive = true, IsSystem = true, CreatedAt = DateTime.UtcNow },
        new() { Id = Guid.NewGuid(), Name = "Contract", Description = "Contrats et accords", Icon = "📜", Color = "#8b5cf6", SortOrder = 1, IsActive = true, IsSystem = true, CreatedAt = DateTime.UtcNow },
        new() { Id = Guid.NewGuid(), Name = "Report", Description = "Rapports et analyses", Icon = "📊", Color = "#10b981", SortOrder = 2, IsActive = true, IsSystem = true, CreatedAt = DateTime.UtcNow },
        new() { Id = Guid.NewGuid(), Name = "Letter", Description = "Courriers et communications", Icon = "✉️", Color = "#f59e0b", SortOrder = 3, IsActive = true, IsSystem = true, CreatedAt = DateTime.UtcNow },
        new() { Id = Guid.NewGuid(), Name = "Memo", Description = "Mémos et notes internes", Icon = "📝", Color = "#06b6d4", SortOrder = 4, IsActive = true, IsSystem = true, CreatedAt = DateTime.UtcNow },
        new() { Id = Guid.NewGuid(), Name = "Presentation", Description = "Présentations", Icon = "📽️", Color = "#ec4899", SortOrder = 5, IsActive = true, IsSystem = true, CreatedAt = DateTime.UtcNow },
        new() { Id = Guid.NewGuid(), Name = "Spreadsheet", Description = "Tableurs et données", Icon = "📈", Color = "#22c55e", SortOrder = 6, IsActive = true, IsSystem = true, CreatedAt = DateTime.UtcNow },
        new() { Id = Guid.NewGuid(), Name = "Image", Description = "Images et photos", Icon = "🖼️", Color = "#a855f7", SortOrder = 7, IsActive = true, IsSystem = true, CreatedAt = DateTime.UtcNow },
        new() { Id = Guid.NewGuid(), Name = "Other", Description = "Autres documents", Icon = "📎", Color = "#64748b", SortOrder = 8, IsActive = true, IsSystem = true, CreatedAt = DateTime.UtcNow },
    };

    private static List<TaxonomyTag> GetDefaultTags() => new()
    {
        new() { Id = Guid.NewGuid(), Name = "urgent",     Description = "Document urgent", Color = "#ef4444", UsageCount = 0, IsSystem = true, IsActive = true, CreatedAt = DateTime.UtcNow },
        new() { Id = Guid.NewGuid(), Name = "confidential", Description = "Document confidentiel", Color = "#dc2626", UsageCount = 0, IsSystem = true, IsActive = true, CreatedAt = DateTime.UtcNow },
        new() { Id = Guid.NewGuid(), Name = "reviewed",   Description = "Document relu et validé", Color = "#16a34a", UsageCount = 0, IsSystem = true, IsActive = true, CreatedAt = DateTime.UtcNow },
        new() { Id = Guid.NewGuid(), Name = "draft",      Description = "Brouillon", Color = "#f59e0b", UsageCount = 0, IsSystem = true, IsActive = true, CreatedAt = DateTime.UtcNow },
        new() { Id = Guid.NewGuid(), Name = "archived",   Description = "Archivé", Color = "#6b7280", UsageCount = 0, IsSystem = true, IsActive = true, CreatedAt = DateTime.UtcNow },
    };
}

// ── Models ────────────────────────────────────────────────────────────────────

public class TaxonomyCategory
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Icon { get; set; } = "📁";
    public string Color { get; set; } = "#6366f1";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class TaxonomyTag
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string Color { get; set; } = "#64748b";
    public int UsageCount { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class TaxonomyTagList
{
    public List<TaxonomyTag> Tags { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class TaxonomyData
{
    public List<TaxonomyCategory> Categories { get; set; } = new();
    public List<TaxonomyTag> Tags { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
}

// ── Request models ─────────────────────────────────────────────────────────────

public class CreateCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
}

public class UpdateCategoryRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public int? SortOrder { get; set; }
    public bool? IsActive { get; set; }
}

public class CreateTagRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Color { get; set; }
}

public class UpdateTagRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Color { get; set; }
    public bool? IsActive { get; set; }
}

public class CreateBulkTagsRequest
{
    public List<string> Names { get; set; } = new();
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Color { get; set; }
}

public class TrackTagUsageRequest
{
    public List<string> TagNames { get; set; } = new();
}

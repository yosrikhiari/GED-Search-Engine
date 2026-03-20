using System.ComponentModel.DataAnnotations;

namespace GED.Core.Models;

public class WebhookConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Url { get; set; } = string.Empty;

    public string? Secret { get; set; }

    public List<string> Events { get; set; } = new()
    {
        "document.created",
        "document.updated",
        "document.deleted",
        "document.access_granted",
        "document.access_revoked"
    };

    public bool IsActive { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 30;

    public int MaxRetries { get; set; } = 3;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastTriggeredAt { get; set; }

    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }
}

public class WebhookPayload
{
    public string Event { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? CorrelationId { get; set; }
    public WebhookDocumentData? Document { get; set; }
    public WebhookAclData? Access { get; set; }
    public object? Metadata { get; set; }
}

public class WebhookDocumentData
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long FileSize { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WebhookAclData
{
    public Guid DocumentId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? GroupId { get; set; }
    public string? Permission { get; set; }
    public string? GrantedBy { get; set; }
}

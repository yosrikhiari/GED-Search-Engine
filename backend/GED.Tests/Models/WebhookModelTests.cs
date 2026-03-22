using FluentAssertions;
using GED.Core.Models;

namespace GED.Tests.Models;

public class WebhookModelTests
{
    [Fact]
    public void WebhookConfig_DefaultValues_AreCorrect()
    {
        var config = new WebhookConfig();

        config.Id.Should().NotBe(Guid.Empty);
        config.Name.Should().BeEmpty();
        config.Url.Should().BeEmpty();
        config.Secret.Should().BeNull();
        config.Events.Should().NotBeEmpty();
        config.IsActive.Should().BeTrue();
        config.TimeoutSeconds.Should().Be(30);
        config.MaxRetries.Should().Be(3);
        config.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        config.LastTriggeredAt.Should().BeNull();
        config.SuccessCount.Should().Be(0);
        config.FailureCount.Should().Be(0);
    }

    [Fact]
    public void WebhookConfig_WithFullData_SetsAllProperties()
    {
        var now = DateTime.UtcNow;
        var config = new WebhookConfig
        {
            Id = Guid.NewGuid(),
            Name = "Test Webhook",
            Url = "https://example.com/webhook",
            Secret = "secret-key",
            Events = new List<string> { "document.created" },
            IsActive = false,
            TimeoutSeconds = 60,
            MaxRetries = 5,
            CreatedAt = now,
            LastTriggeredAt = now,
            SuccessCount = 10,
            FailureCount = 2
        };

        config.Name.Should().Be("Test Webhook");
        config.Url.Should().Be("https://example.com/webhook");
        config.Events.Should().Contain("document.created");
    }

    [Fact]
    public void WebhookPayload_DefaultValues_AreCorrect()
    {
        var payload = new WebhookPayload();

        payload.Event.Should().BeEmpty();
        payload.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        payload.CorrelationId.Should().BeNull();
        payload.Document.Should().BeNull();
        payload.Access.Should().BeNull();
        payload.Metadata.Should().BeNull();
    }

    [Fact]
    public void WebhookPayload_WithData_SetsAllProperties()
    {
        var payload = new WebhookPayload
        {
            Event = "document.created",
            CorrelationId = Guid.NewGuid().ToString(),
            Document = new WebhookDocumentData
            {
                Id = Guid.NewGuid(),
                Title = "Test Doc",
                Category = "Invoice"
            },
            Metadata = new { key = "value" }
        };

        payload.Event.Should().Be("document.created");
        payload.Document.Should().NotBeNull();
        payload.Document!.Title.Should().Be("Test Doc");
    }

    [Fact]
    public void WebhookDocumentData_DefaultValues_AreCorrect()
    {
        var data = new WebhookDocumentData();

        data.Id.Should().Be(Guid.Empty);
        data.Title.Should().BeEmpty();
        data.Category.Should().BeNull();
        data.FileName.Should().BeNull();
        data.ContentType.Should().BeNull();
        data.FileSize.Should().Be(0);
        data.CreatedBy.Should().BeNull();
        data.CreatedAt.Should().Be(default);
    }

    [Fact]
    public void WebhookDocumentData_WithData_SetsAllProperties()
    {
        var now = DateTime.UtcNow;
        var data = new WebhookDocumentData
        {
            Id = Guid.NewGuid(),
            Title = "Invoice 2024",
            Category = "Invoice",
            FileName = "invoice_2024.pdf",
            ContentType = "application/pdf",
            FileSize = 1024 * 500,
            CreatedBy = "admin",
            CreatedAt = now
        };

        data.Title.Should().Be("Invoice 2024");
        data.FileSize.Should().Be(512000);
    }

    [Fact]
    public void WebhookAclData_DefaultValues_AreCorrect()
    {
        var data = new WebhookAclData();

        data.DocumentId.Should().Be(Guid.Empty);
        data.UserId.Should().BeNull();
        data.GroupId.Should().BeNull();
        data.Permission.Should().BeNull();
        data.GrantedBy.Should().BeNull();
    }

    [Fact]
    public void WebhookAclData_WithData_SetsAllProperties()
    {
        var data = new WebhookAclData
        {
            DocumentId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            GroupId = Guid.NewGuid(),
            Permission = "read",
            GrantedBy = "admin"
        };

        data.Permission.Should().Be("read");
        data.GrantedBy.Should().Be("admin");
    }
}
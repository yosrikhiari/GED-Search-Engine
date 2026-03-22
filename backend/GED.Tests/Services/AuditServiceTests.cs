using FluentAssertions;
using GED.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GED.Tests.Services;

public class AuditServiceTests
{
    private readonly Mock<ILogger<AuditService>> _loggerMock;
    private readonly AuditService _service;

    public AuditServiceTests()
    {
        _loggerMock = new Mock<ILogger<AuditService>>();
        _service = new AuditService(_loggerMock.Object);
    }

    [Fact]
    public void LogAudit_WithValidEvent_LogsInformation()
    {
        var auditEvent = new AuditEvent
        {
            EventType = "DOCUMENT_CREATE",
            PerformedBy = "admin",
            TargetType = "Document",
            TargetId = Guid.NewGuid().ToString(),
            Action = "Created",
            Details = "Test document created",
            Success = true
        };

        _service.LogAudit(auditEvent);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            Times.Once);
    }

    [Fact]
    public void LogAudit_WithFailedEvent_LogsWarning()
    {
        var auditEvent = new AuditEvent
        {
            EventType = "DOCUMENT_DELETE",
            PerformedBy = "admin",
            TargetType = "Document",
            TargetId = Guid.NewGuid().ToString(),
            Action = "Delete",
            Success = false,
            Details = "Access denied"
        };

        _service.LogAudit(auditEvent);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            Times.Once);
    }

    [Fact]
    public void LogLogin_WithSuccess_LogsInformation()
    {
        _service.LogLogin("admin", true);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            Times.Once);
    }

    [Fact]
    public void LogLogin_WithFailure_LogsWarning()
    {
        _service.LogLogin("admin", false, "Invalid password");

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            Times.Once);
    }

    [Fact]
    public void LogDocumentDelete_LogsInformation()
    {
        var docId = Guid.NewGuid();
        _service.LogDocumentDelete(docId, "admin", "User requested");

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            Times.Once);
    }

    [Fact]
    public void LogPermissionChange_LogsInformation()
    {
        var docId = Guid.NewGuid();
        _service.LogPermissionChange(docId, "admin", "user1", "Grant");

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            Times.Once);
    }

    [Fact]
    public void LogUserManagement_LogsInformation()
    {
        _service.LogUserManagement("admin", "Create", "newuser", "Created new user account");

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            Times.Once);
    }
}

public class AuditEventTests
{
    [Fact]
    public void AuditEvent_DefaultValues_AreCorrect()
    {
        var evt = new AuditEvent();

        evt.Id.Should().NotBeEmpty();
        evt.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        evt.EventType.Should().BeEmpty();
        evt.PerformedBy.Should().BeEmpty();
        evt.TargetType.Should().BeEmpty();
        evt.Action.Should().BeEmpty();
        evt.Success.Should().BeTrue();
    }

    [Fact]
    public void AuditEvent_WithFullData_SetsAllProperties()
    {
        var now = DateTime.UtcNow;
        var evt = new AuditEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = now,
            EventType = "TEST",
            PerformedBy = "admin",
            TargetType = "Document",
            TargetId = Guid.NewGuid().ToString(),
            Action = "Update",
            Details = "Test details",
            IpAddress = "192.168.1.1",
            Success = true
        };

        evt.Id.Should().NotBe(Guid.Empty);
        evt.Timestamp.Should().Be(now);
        evt.EventType.Should().Be("TEST");
        evt.PerformedBy.Should().Be("admin");
        evt.TargetId.Should().NotBeNull();
        evt.IpAddress.Should().Be("192.168.1.1");
    }
}
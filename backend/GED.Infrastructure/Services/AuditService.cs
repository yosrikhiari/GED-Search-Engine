using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

/// <summary>
/// Audit service for logging sensitive operations.
/// Records actions like user management, permission changes, document deletion, etc.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Logs an audit event for sensitive operations.
    /// </summary>
    void LogAudit(AuditEvent auditEvent);

    /// <summary>
    /// Logs user login attempt.
    /// </summary>
    void LogLogin(string username, bool success, string? reason = null);

    /// <summary>
    /// Logs document deletion.
    /// </summary>
    void LogDocumentDelete(Guid documentId, string performedBy, string? reason = null);

    /// <summary>
    /// Logs permission change.
    /// </summary>
    void LogPermissionChange(Guid documentId, string performedBy, string targetUser, string action);

    /// <summary>
    /// Logs user management action.
    /// </summary>
    void LogUserManagement(string performedBy, string action, string targetUser, string? details = null);
}

/// <summary>
/// Represents an audit event with structured data.
/// </summary>
public class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string EventType { get; set; } = string.Empty;
    public string PerformedBy { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public bool Success { get; set; } = true;
}

/// <summary>
/// Implementation of audit service using structured logging.
/// </summary>
public class AuditService : IAuditService
{
    private readonly ILogger<AuditService> _logger;

    public AuditService(ILogger<AuditService> logger)
    {
        _logger = logger;
    }

    public void LogAudit(AuditEvent auditEvent)
    {
        var logLevel = auditEvent.Success ? LogLevel.Information : LogLevel.Warning;
        
        _logger.Log(logLevel,
            "AUDIT: {EventType} | By={PerformedBy} | Target={TargetType}:{TargetId} | Action={Action} | Details={Details} | IP={IpAddress}",
            auditEvent.EventType,
            auditEvent.PerformedBy,
            auditEvent.TargetType,
            auditEvent.TargetId ?? "N/A",
            auditEvent.Action,
            auditEvent.Details ?? "N/A",
            auditEvent.IpAddress ?? "Unknown");
    }

    public void LogLogin(string username, bool success, string? reason = null)
    {
        var level = success ? LogLevel.Information : LogLevel.Warning;
        
        _logger.Log(level,
            "AUDIT: LOGIN | User={Username} | Success={Success} | Reason={Reason}",
            username,
            success,
            reason ?? "N/A");
    }

    public void LogDocumentDelete(Guid documentId, string performedBy, string? reason = null)
    {
        _logger.LogInformation(
            "AUDIT: DOCUMENT_DELETE | DocId={DocumentId} | By={PerformedBy} | Reason={Reason}",
            documentId,
            performedBy,
            reason ?? "N/A");
    }

    public void LogPermissionChange(Guid documentId, string performedBy, string targetUser, string action)
    {
        _logger.LogInformation(
            "AUDIT: PERMISSION_CHANGE | DocId={DocumentId} | By={PerformedBy} | TargetUser={TargetUser} | Action={Action}",
            documentId,
            performedBy,
            targetUser,
            action);
    }

    public void LogUserManagement(string performedBy, string action, string targetUser, string? details = null)
    {
        _logger.LogInformation(
            "AUDIT: USER_MANAGEMENT | By={PerformedBy} | Action={Action} | Target={TargetUser} | Details={Details}",
            performedBy,
            action,
            targetUser,
            details ?? "N/A");
    }
}
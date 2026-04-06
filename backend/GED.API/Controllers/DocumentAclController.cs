using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Data;
using GED.Infrastructure.Services;
using GED.API.Models;
using System.Security.Claims;

namespace GED.API.Controllers;

/// <summary>
/// Document ACL controller.
///
/// After every grant or revoke the affected document is re-indexed so that
/// OpenSearch's allowedUserIds field stays in sync with the DB immediately.
/// No eventual-consistency lag: access takes effect on the very next search.
/// </summary>
[ApiController]
[Route("api/document-permissions")]
[Authorize]
public class DocumentAclController : ControllerBase
{
    private readonly GedDbContext                       _db;
    private readonly IAuthService                       _authService;
    private readonly ISearchService                     _searchService;
    private readonly IDocumentService                   _documentService;
    private readonly ILogger<DocumentAclController>     _logger;

    public DocumentAclController(
        GedDbContext                   db,
        IAuthService                   authService,
        ISearchService                 searchService,
        IDocumentService               documentService,
        ILogger<DocumentAclController>  logger)
    {
        _db              = db;
        _authService     = authService;
        _searchService   = searchService;
        _documentService = documentService;
        _logger          = logger;
    }

    // ── Admin: list ACL entries for a document ────────────────────────────────

    [HttpGet("{documentId:guid}/acl")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<DocumentAclDto>>> GetAcl(Guid documentId)
    {
        var acls     = await _db.DocumentAcls
            .Where(a => a.DocumentId == documentId)
            .ToListAsync();

        var userIds = acls.Select(a => a.UserId).Distinct().ToList();
        var allUsers = _authService.GetAllUsers()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionary(u => u.Id);

        var result = acls.Select(a =>
        {
            allUsers.TryGetValue(a.UserId, out var user);
            return new DocumentAclDto
            {
                Id         = a.Id,
                DocumentId = a.DocumentId,
                UserId     = a.UserId,
                Username   = user?.Username ?? "(unknown)",
                FullName   = user?.FullName,
                Permission = a.Permission,
                GrantedAt  = a.GrantedAt,
                ExpiresAt  = a.ExpiresAt,
                IsActive   = a.IsActive
            };
        }).ToList();

        return Ok(result);
    }

    // ── Admin: grant access ───────────────────────────────────────────────────

    [HttpPost("{documentId:guid}/acl")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<DocumentAclDto>> GrantAccess(
        Guid documentId,
        [FromBody] GrantAccessRequest request)
    {
        var adminId = GetCurrentUserId();
        if (adminId == null) return Unauthorized();

        if (request.ExpiresAt.HasValue && request.ExpiresAt.Value <= DateTime.UtcNow)
            return BadRequest(ErrorResponse.Create("ExpiresAt must be in the future."));

        var existing = await _db.DocumentAcls
            .FirstOrDefaultAsync(a => a.DocumentId == documentId && a.UserId == request.UserId);

        if (existing != null)
        {
            existing.Permission = request.Permission;
            existing.ExpiresAt  = request.ExpiresAt;
            existing.GrantedAt  = DateTime.UtcNow;
            existing.GrantedBy  = adminId.Value;
        }
        else
        {
            _db.DocumentAcls.Add(new DocumentAcl
            {
                Id         = Guid.NewGuid(),
                DocumentId = documentId,
                UserId     = request.UserId,
                Permission = request.Permission,
                GrantedAt  = DateTime.UtcNow,
                GrantedBy  = adminId.Value,
                ExpiresAt  = request.ExpiresAt
            });
        }

        await _db.SaveChangesAsync();

        // Re-index the document so allowedUserIds is updated in OpenSearch
        var doc = await _documentService.GetDocumentByIdAsync(documentId);
        if (doc != null)
        {
            await _searchService.UpdateDocumentIndexAsync(doc);
            _logger.LogInformation(
                "Re-indexed document {DocId} after ACL grant for user {UserId}",
                documentId, request.UserId);
        }

        var user = _authService.GetUserById(request.UserId);

        _logger.LogInformation(
            "Admin {Admin} granted {Permission} access to document {DocId} for user {UserId} (expires: {Expires})",
            adminId, request.Permission, documentId, request.UserId,
            request.ExpiresAt?.ToString("yyyy-MM-dd") ?? "never");

        return Ok(new DocumentAclDto
        {
            Id         = existing?.Id ?? Guid.NewGuid(),
            DocumentId = documentId,
            UserId     = request.UserId,
            Username   = user?.Username ?? "(unknown)",
            FullName   = user?.FullName,
            Permission = request.Permission,
            GrantedAt  = DateTime.UtcNow,
            ExpiresAt  = request.ExpiresAt,
            IsActive   = true
        });
    }

    // ── Admin: revoke access ──────────────────────────────────────────────────

    [HttpDelete("{documentId:guid}/acl/{aclId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RevokeAccess(Guid documentId, Guid aclId)
    {
        var acl = await _db.DocumentAcls
            .FirstOrDefaultAsync(a => a.Id == aclId && a.DocumentId == documentId);

        if (acl == null) return NotFound(ErrorResponse.Create("ACL entry not found"));

        _db.DocumentAcls.Remove(acl);
        await _db.SaveChangesAsync();

        // Re-index so the removed user is cleared from allowedUserIds
        var doc = await _documentService.GetDocumentByIdAsync(documentId);
        if (doc != null)
        {
            await _searchService.UpdateDocumentIndexAsync(doc);
            _logger.LogInformation(
                "Re-indexed document {DocId} after ACL revoke (entry {AclId})",
                documentId, aclId);
        }

        _logger.LogInformation("ACL entry {AclId} revoked for document {DocId}", aclId, documentId);
        return Ok(ErrorResponse.Create("Access revoked."));
    }

    // ── User: list my accessible documents ────────────────────────────────────

    [HttpGet("my-access")]
    public async Task<ActionResult<List<object>>> GetMyAccessibleDocuments()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var now    = DateTime.UtcNow;
        var grants = await _db.DocumentAcls
            .Where(a => a.UserId == userId.Value
                     && (a.ExpiresAt == null || a.ExpiresAt > now))
            .ToListAsync();

        var docIds    = grants.Select(g => g.DocumentId).Distinct().ToList();
        var documents = await _db.Documents
            .Where(d => docIds.Contains(d.Id))
            .ToListAsync();

        var result = documents.Select(d =>
        {
            var grant = grants.First(g => g.DocumentId == d.Id);
            return new
            {
                d.Id, d.Title, d.FileName, d.ContentType,
                d.FileSize, d.Category, d.Status,
                d.CreatedAt, d.DocumentDate, d.Tags, d.Description,
                AccessGrant = new
                {
                    grant.Permission,
                    grant.GrantedAt,
                    grant.ExpiresAt,
                    IsPermanent   = grant.ExpiresAt == null,
                    ExpiresInDays = grant.ExpiresAt.HasValue
                        ? (int)(grant.ExpiresAt.Value - now).TotalDays
                        : (int?)null
                }
            };
        });

        return Ok(result);
    }

    // ── Batch ACL operations ────────────────────────────────────────────────────

    /// <summary>
    /// Bulk grant access to multiple documents for multiple users.
    /// </summary>
    [HttpPost("acl/batch")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<BatchAclResult>> BatchGrantAccess(
        [FromBody] BatchAclRequest request)
    {
        var adminId = GetCurrentUserId();
        if (adminId == null) return Unauthorized();

        if ((request.DocumentIds == null || !request.DocumentIds.Any()) &&
            (request.UserIds == null || !request.UserIds.Any()))
            return BadRequest(ErrorResponse.Create("Provide at least documentIds or userIds."));

        // Cross-product: grant every user access to every document
        var docIds = request.DocumentIds?.Distinct().ToList() ?? new List<Guid>();
        var userIds = request.UserIds?.Distinct().ToList() ?? new List<Guid>();

        if (docIds.Count == 0 || userIds.Count == 0)
            return BadRequest(ErrorResponse.Create("Both documentIds and userIds must be provided for batch grant."));

        if (docIds.Count * userIds.Count > 500)
            return BadRequest(ErrorResponse.Create("Maximum 500 access grants per request."));

        var totalRequested = docIds.Count * userIds.Count;
        var tasks = new List<Task<SingleAclResult>>();
        var affectedDocIds = new HashSet<Guid>();

        foreach (var docId in docIds)
        {
            foreach (var userId in userIds)
            {
                tasks.Add(GrantSingleAclAsync(docId, userId, request.Permission, request.ExpiresAt, adminId.Value, affectedDocIds));
            }
        }

        var taskResults = await Task.WhenAll(tasks);

        // Batch re-index all affected documents (N+1 fix)
        await ReindexDocumentsAsync(affectedDocIds);

        var results = new BatchAclResult 
        { 
            TotalRequested = totalRequested, 
            Succeeded = taskResults.Count(r => r.Succeeded), 
            Failed = taskResults.Count(r => !r.Succeeded),
            Errors = taskResults.Where(r => !r.Succeeded && r.Error != null).Select(r => r.Error!).ToList()
        };

        _logger.LogInformation(
            "Batch ACL grant: {Total} operations, {Succeeded} succeeded, {Failed} failed",
            results.TotalRequested, results.Succeeded, results.Failed);

        return Ok(results);
    }

    /// <summary>
    /// Bulk revoke access from multiple users across multiple documents.
    /// </summary>
    [HttpDelete("acl/batch")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<BatchAclResult>> BatchRevokeAccess(
        [FromBody] BatchRevokeRequest request)
    {
        var adminId = GetCurrentUserId();
        if (adminId == null) return Unauthorized();

        if (request.AclIds == null || !request.AclIds.Any())
            return BadRequest(ErrorResponse.Create("Provide ACL entry IDs to revoke."));

        if (request.AclIds.Count > 500)
            return BadRequest(ErrorResponse.Create("Maximum 500 revocations per request."));

        var results = new BatchAclResult { TotalRequested = request.AclIds.Count, Succeeded = 0, Failed = 0 };
        var revokedDocIds = new HashSet<Guid>();

        foreach (var aclId in request.AclIds)
        {
            try
            {
                var acl = await _db.DocumentAcls.FirstOrDefaultAsync(a => a.Id == aclId);
                if (acl == null)
                {
                    results.Failed++;
                    results.Errors.Add($"ACL {aclId}: not found");
                    continue;
                }

                _db.DocumentAcls.Remove(acl);
                revokedDocIds.Add(acl.DocumentId);
                results.Succeeded++;
            }
            catch (Exception ex)
            {
                results.Failed++;
                results.Errors.Add($"ACL {aclId}: {ex.Message}");
            }
        }

        await _db.SaveChangesAsync();

        // Re-index affected documents in batch
        if (revokedDocIds.Any())
        {
            var docs = await _documentService.GetDocumentsByIdsAsync(revokedDocIds.ToList());
            if (docs.Any())
                await _searchService.BulkIndexDocumentsAsync(docs);
        }

        _logger.LogInformation(
            "Batch ACL revoke: {Total} requested, {Succeeded} succeeded, {Failed} failed",
            results.TotalRequested, results.Succeeded, results.Failed);

        return Ok(results);
    }

    /// <summary>
    /// Grant access to multiple users for a single document.
    /// </summary>
    [HttpPost("{documentId:guid}/acl/users")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<BatchAclResult>> GrantAccessToUsers(
        Guid documentId,
        [FromBody] GrantAccessToUsersRequest request)
    {
        var adminId = GetCurrentUserId();
        if (adminId == null) return Unauthorized();

        if (request.UserIds == null || !request.UserIds.Any())
            return BadRequest(ErrorResponse.Create("Provide user IDs."));

        if (request.UserIds.Count > 100)
            return BadRequest(ErrorResponse.Create("Maximum 100 users per request."));

        var affectedDocIds = new HashSet<Guid> { documentId };
        var tasks = request.UserIds.Select(userId => 
            GrantSingleAclAsync(documentId, userId, request.Permission, request.ExpiresAt, adminId.Value, affectedDocIds));
        var taskResults = await Task.WhenAll(tasks);

        // Batch-save all ACL changes
        await _db.SaveChangesAsync();

        // Batch re-index (N+1 fix)
        await ReindexDocumentsAsync(affectedDocIds);

        var results = new BatchAclResult 
        { 
            TotalRequested = request.UserIds.Count, 
            Succeeded = taskResults.Count(r => r.Succeeded), 
            Failed = taskResults.Count(r => !r.Succeeded),
            Errors = taskResults.Where(r => !r.Succeeded && r.Error != null).Select(r => r.Error!).ToList()
        };

        return Ok(results);
    }

    /// <summary>
    /// Bulk revoke all access to documents matching a filter.
    /// </summary>
    [HttpPost("acl/bulk-revoke")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<BatchAclResult>> BulkRevokeByFilter(
        [FromBody] BulkRevokeByFilterRequest request)
    {
        var adminId = GetCurrentUserId();
        if (adminId == null) return Unauthorized();

        var query = _db.DocumentAcls.AsQueryable();

        if (request.DocumentIds?.Any() == true)
            query = query.Where(a => request.DocumentIds.Contains(a.DocumentId));

        if (request.UserIds?.Any() == true)
            query = query.Where(a => request.UserIds.Contains(a.UserId));

        if (request.ExpiredOnly == true)
            query = query.Where(a => a.ExpiresAt != null && a.ExpiresAt <= DateTime.UtcNow);

        var toRemove = await query.ToListAsync();

        if (toRemove.Count == 0)
            return Ok(new BatchAclResult { TotalRequested = 0, Succeeded = 0, Failed = 0 });

        var docIds = toRemove.Select(a => a.DocumentId).Distinct().ToHashSet();
        _db.DocumentAcls.RemoveRange(toRemove);
        await _db.SaveChangesAsync();

        // Batch re-index all affected documents (N+1 fix)
        await ReindexDocumentsAsync(docIds);

        return Ok(new BatchAclResult
        {
            TotalRequested = toRemove.Count,
            Succeeded = toRemove.Count,
            Failed = 0
        });
    }

    private async Task<SingleAclResult> GrantSingleAclAsync(
        Guid docId, Guid userId, int permission, DateTime? expiresAt, Guid adminId, HashSet<Guid> affectedDocIds, bool saveChanges = false)
    {
        try
        {
            var existing = await _db.DocumentAcls
                .FirstOrDefaultAsync(a => a.DocumentId == docId && a.UserId == userId);

            var aclPermission = permission switch
            {
                >= 3 => AclPermission.FullControl,
                2 => AclPermission.Delete,
                1 => AclPermission.Write,
                _ => AclPermission.Read
            };

            if (existing != null)
            {
                existing.Permission = aclPermission;
                existing.ExpiresAt = expiresAt;
                existing.GrantedAt = DateTime.UtcNow;
                existing.GrantedBy = adminId;
            }
            else
            {
                _db.DocumentAcls.Add(new DocumentAcl
                {
                    Id = Guid.NewGuid(),
                    DocumentId = docId,
                    UserId = userId,
                    Permission = aclPermission,
                    GrantedAt = DateTime.UtcNow,
                    GrantedBy = adminId,
                    ExpiresAt = expiresAt
                });
            }

            affectedDocIds.Add(docId);

            return new SingleAclResult { Succeeded = true };
        }
        catch (Exception ex)
        {
            return new SingleAclResult 
            { 
                Succeeded = false, 
                Error = $"Doc {docId} / User {userId}: {ex.Message}" 
            };
        }
    }

    private async Task ReindexDocumentsAsync(HashSet<Guid> docIds)
    {
        if (docIds.Count == 0) return;

        var documents = await _documentService.GetDocumentsByIdsAsync(docIds);
        if (documents.Count != 0)
        {
            await _searchService.BulkIndexDocumentsAsync(documents);
            _logger.LogInformation("Re-indexed {Count} documents after ACL changes", documents.Count);
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

// ── Batch ACL request/result models ─────────────────────────────────────────

public class BatchAclRequest
{
    public List<Guid>? DocumentIds { get; set; }
    public List<Guid>? UserIds { get; set; }
    public int Permission { get; set; } = 1;
    public DateTime? ExpiresAt { get; set; }
}

public class BatchAclResult
{
    public int TotalRequested { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class BatchRevokeRequest
{
    public List<Guid> AclIds { get; set; } = new();
}

public class GrantAccessToUsersRequest
{
    public List<Guid> UserIds { get; set; } = new();
    public int Permission { get; set; } = 1;
    public DateTime? ExpiresAt { get; set; }
}

public class BulkRevokeByFilterRequest
{
    public List<Guid>? DocumentIds { get; set; }
    public List<Guid>? UserIds { get; set; }
    public bool? ExpiredOnly { get; set; }
}

public class SingleAclResult
{
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
}
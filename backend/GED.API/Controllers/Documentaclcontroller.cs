using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Data;
using GED.Infrastructure.Services;
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
[Route("api/documents")]
[Authorize]
public class DocumentAclController : ControllerBase
{
    private readonly GedDbContext                       _db;
    private readonly AuthService                        _authService;
    private readonly ISearchService                     _searchService;
    private readonly IDocumentService                   _documentService;
    private readonly ILogger<DocumentAclController>     _logger;

    public DocumentAclController(
        GedDbContext                   db,
        AuthService                    authService,
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
        var allUsers = _authService.GetAllUsers().ToDictionary(u => u.Id);

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
            return BadRequest(new { error = "ExpiresAt must be in the future." });

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

        if (acl == null) return NotFound();

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
        return Ok(new { message = "Access revoked." });
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

    // ── Helper ────────────────────────────────────────────────────────────────

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
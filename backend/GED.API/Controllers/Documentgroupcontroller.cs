using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using GED.Core.Models;
using GED.Infrastructure.Data;
using GED.Infrastructure.Services;
using System.Security.Claims;

namespace GED.API.Controllers;

/// <summary>
/// Document Group management — Admin only.
///
/// A "group" is a named bundle of documents (e.g. "RH – Contracts 2024").
/// The admin can:
///   - CRUD groups, add/remove documents from them
///   - Assign a group to a user (expands to DocumentAcl rows automatically)
///   - Revoke a group assignment (removes the expanded ACL rows)
///   - Get a full access summary per user
///
/// Endpoints:
///   GET    /api/groups                              → list all groups (with counts)
///   POST   /api/groups                              → create group
///   GET    /api/groups/{id}                         → group detail (docs + assignees)
///   PUT    /api/groups/{id}                         → update group metadata
///   DELETE /api/groups/{id}                         → deactivate group
///   POST   /api/groups/{id}/documents               → add documents to group
///   DELETE /api/groups/{id}/documents/{memberId}    → remove document from group
///   POST   /api/groups/{id}/assign                  → assign group to user
///   DELETE /api/groups/{id}/assignments/{assignId}  → revoke assignment
///   GET    /api/groups/users/access-summary         → all users with their access
///   GET    /api/groups/users/{userId}/access        → one user's full access
/// </summary>
[ApiController]
[Route("api/groups")]
[Authorize(Roles = "Admin")]
public class DocumentGroupController : ControllerBase
{
    private readonly GedDbContext _db;
    private readonly AuthService _authService;
    private readonly ILogger<DocumentGroupController> _logger;

    public DocumentGroupController(
        GedDbContext db,
        AuthService authService,
        ILogger<DocumentGroupController> logger)
    {
        _db          = db;
        _authService = authService;
        _logger      = logger;
    }

    // ── List groups ───────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<List<DocumentGroupDto>>> GetGroups()
    {
        var groups = await _db.DocumentGroups
            .Where(g => g.IsActive)
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync();

        var groupIds = groups.Select(g => g.Id).ToList();

        var docCounts = await _db.DocumentGroupMembers
            .Where(m => groupIds.Contains(m.GroupId))
            .GroupBy(m => m.GroupId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var userCounts = await _db.UserGroupAssignments
            .Where(a => groupIds.Contains(a.GroupId)
                     && (a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow))
            .GroupBy(a => a.GroupId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var result = groups.Select(g => new DocumentGroupDto
        {
            Id            = g.Id,
            Name          = g.Name,
            Description   = g.Description,
            Color         = g.Color,
            Icon          = g.Icon,
            Category      = g.Category,
            CreatedAt     = g.CreatedAt,
            IsActive      = g.IsActive,
            DocumentCount = docCounts.GetValueOrDefault(g.Id, 0),
            UserCount     = userCounts.GetValueOrDefault(g.Id, 0)
        }).ToList();

        return Ok(result);
    }

    // ── Create group ──────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<DocumentGroupDto>> CreateGroup([FromBody] CreateGroupRequest req)
    {
        var adminId = GetCurrentUserId();
        if (adminId == null) return Unauthorized();

        var group = new DocumentGroup
        {
            Id          = Guid.NewGuid(),
            Name        = req.Name,
            Description = req.Description,
            Color       = req.Color ?? "#2563eb",
            Icon        = req.Icon ?? "📁",
            Category    = req.Category,
            CreatedBy   = adminId.Value,
            CreatedAt   = DateTime.UtcNow,
            IsActive    = true
        };

        _db.DocumentGroups.Add(group);

        // Optionally seed documents
        if (req.DocumentIds?.Any() == true)
        {
            var validDocIds = await _db.Documents
                .Where(d => req.DocumentIds.Contains(d.Id))
                .Select(d => d.Id)
                .ToListAsync();

            foreach (var docId in validDocIds)
            {
                _db.DocumentGroupMembers.Add(new DocumentGroupMember
                {
                    Id         = Guid.NewGuid(),
                    GroupId    = group.Id,
                    DocumentId = docId,
                    AddedAt    = DateTime.UtcNow,
                    AddedBy    = adminId.Value
                });
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Admin {Admin} created document group '{Name}' ({Id})",
            adminId, group.Name, group.Id);

        return Ok(new DocumentGroupDto
        {
            Id            = group.Id,
            Name          = group.Name,
            Description   = group.Description,
            Color         = group.Color,
            Icon          = group.Icon,
            Category      = group.Category,
            CreatedAt     = group.CreatedAt,
            IsActive      = true,
            DocumentCount = req.DocumentIds?.Count ?? 0,
            UserCount     = 0
        });
    }

    // ── Get single group (with documents + assignees) ─────────────────────────

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentGroupDto>> GetGroup(Guid id)
    {
        var group = await _db.DocumentGroups.FindAsync(id);
        if (group == null || !group.IsActive) return NotFound();

        var members = await _db.DocumentGroupMembers
            .Where(m => m.GroupId == id)
            .ToListAsync();

        var docIds = members.Select(m => m.DocumentId).Distinct().ToList();
        var docs = await _db.Documents
            .Where(d => docIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Title, d.FileName, d.Category })
            .ToListAsync();
        var docLookup = docs.ToDictionary(d => d.Id);

        var assignments = await _db.UserGroupAssignments
            .Where(a => a.GroupId == id)
            .OrderByDescending(a => a.AssignedAt)
            .ToListAsync();

        var allUsers = _authService.GetAllUsers().ToDictionary(u => u.Id);

        return Ok(new DocumentGroupDto
        {
            Id          = group.Id,
            Name        = group.Name,
            Description = group.Description,
            Color       = group.Color,
            Icon        = group.Icon,
            Category    = group.Category,
            CreatedAt   = group.CreatedAt,
            IsActive    = group.IsActive,
            DocumentCount = members.Count,
            UserCount   = assignments.Count(a => a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow),

            Documents = members.Select(m =>
            {
                docLookup.TryGetValue(m.DocumentId, out var doc);
                return new GroupDocumentDto
                {
                    MemberId          = m.Id,
                    DocumentId        = m.DocumentId,
                    Title             = doc?.Title ?? "(unknown)",
                    FileName          = doc?.FileName ?? "",
                    Category          = doc?.Category,
                    DefaultPermission = m.DefaultPermission,
                    AddedAt           = m.AddedAt
                };
            }).ToList(),

            Assignments = assignments.Select(a =>
            {
                allUsers.TryGetValue(a.UserId, out var user);
                return new GroupAssignmentDto
                {
                    AssignmentId = a.Id,
                    UserId       = a.UserId,
                    Username     = user?.Username ?? "(unknown)",
                    FullName     = user?.FullName,
                    UserRole     = user?.Role ?? UserRole.User,
                    Permission   = a.Permission,
                    AssignedAt   = a.AssignedAt,
                    ExpiresAt    = a.ExpiresAt
                };
            }).ToList()
        });
    }

    // ── Update group metadata ─────────────────────────────────────────────────

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateGroup(Guid id, [FromBody] UpdateGroupRequest req)
    {
        var group = await _db.DocumentGroups.FindAsync(id);
        if (group == null || !group.IsActive) return NotFound();

        if (req.Name        != null) group.Name        = req.Name;
        if (req.Description != null) group.Description = req.Description;
        if (req.Color       != null) group.Color       = req.Color;
        if (req.Icon        != null) group.Icon        = req.Icon;
        if (req.Category    != null) group.Category    = req.Category;
        group.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { message = "Group updated." });
    }

    // ── Deactivate group ──────────────────────────────────────────────────────

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid id)
    {
        var group = await _db.DocumentGroups.FindAsync(id);
        if (group == null) return NotFound();

        group.IsActive  = false;
        group.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Admin {Admin} deactivated group {GroupId}", GetCurrentUserId(), id);
        return Ok(new { message = "Group deactivated." });
    }

    // ── Add documents to group ────────────────────────────────────────────────

    [HttpPost("{id:guid}/documents")]
    public async Task<IActionResult> AddDocuments(Guid id, [FromBody] AddDocumentsToGroupRequest req)
    {
        var adminId = GetCurrentUserId();
        if (adminId == null) return Unauthorized();

        var group = await _db.DocumentGroups.FindAsync(id);
        if (group == null || !group.IsActive) return NotFound();

        var existingDocIds = await _db.DocumentGroupMembers
            .Where(m => m.GroupId == id)
            .Select(m => m.DocumentId)
            .ToListAsync();

        var newDocIds = req.DocumentIds.Except(existingDocIds).ToList();

        if (!newDocIds.Any()) return Ok(new { message = "All documents already in group.", added = 0 });

        var validDocIds = await _db.Documents
            .Where(d => newDocIds.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync();

        foreach (var docId in validDocIds)
        {
            _db.DocumentGroupMembers.Add(new DocumentGroupMember
            {
                Id                = Guid.NewGuid(),
                GroupId           = id,
                DocumentId        = docId,
                DefaultPermission = req.DefaultPermission,
                AddedAt           = DateTime.UtcNow,
                AddedBy           = adminId.Value
            });
        }

        group.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Propagate new documents to all existing active assignments
        await PropagateNewDocumentsToAssignments(id, validDocIds, adminId.Value);

        return Ok(new { message = $"{validDocIds.Count} document(s) added.", added = validDocIds.Count });
    }

    // ── Remove document from group ────────────────────────────────────────────

    [HttpDelete("{id:guid}/documents/{memberId:guid}")]
    public async Task<IActionResult> RemoveDocument(Guid id, Guid memberId)
    {
        var member = await _db.DocumentGroupMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.GroupId == id);

        if (member == null) return NotFound();

        _db.DocumentGroupMembers.Remove(member);

        // Remove the ACL rows that were created by group assignments for this document
        var assignedUserIds = await _db.UserGroupAssignments
            .Where(a => a.GroupId == id)
            .Select(a => a.UserId)
            .ToListAsync();

        var aclsToRemove = await _db.DocumentAcls
            .Where(a => a.DocumentId == member.DocumentId
                     && assignedUserIds.Contains(a.UserId))
            .ToListAsync();

        _db.DocumentAcls.RemoveRange(aclsToRemove);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Document removed from group." });
    }

    // ── Assign group to user ──────────────────────────────────────────────────

    [HttpPost("{id:guid}/assign")]
    public async Task<ActionResult<GroupAssignmentDto>> AssignToUser(
        Guid id,
        [FromBody] AssignGroupToUserRequest req)
    {
        var adminId = GetCurrentUserId();
        if (adminId == null) return Unauthorized();

        if (req.ExpiresAt.HasValue && req.ExpiresAt.Value <= DateTime.UtcNow)
            return BadRequest(new { error = "ExpiresAt must be in the future." });

        var group = await _db.DocumentGroups.FindAsync(id);
        if (group == null || !group.IsActive) return NotFound();

        var user = _authService.GetUserById(req.UserId);
        if (user == null) return BadRequest(new { error = "User not found." });

        // Upsert assignment
        var existing = await _db.UserGroupAssignments
            .FirstOrDefaultAsync(a => a.GroupId == id && a.UserId == req.UserId);

        if (existing != null)
        {
            existing.Permission  = req.Permission;
            existing.ExpiresAt   = req.ExpiresAt;
            existing.AssignedAt  = DateTime.UtcNow;
            existing.AssignedBy  = adminId.Value;
        }
        else
        {
            existing = new UserGroupAssignment
            {
                Id         = Guid.NewGuid(),
                GroupId    = id,
                UserId     = req.UserId,
                Permission = req.Permission,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = adminId.Value,
                ExpiresAt  = req.ExpiresAt
            };
            _db.UserGroupAssignments.Add(existing);
        }

        await _db.SaveChangesAsync();

        // Expand: create/update DocumentAcl rows for every document in this group
        var docIds = await _db.DocumentGroupMembers
            .Where(m => m.GroupId == id)
            .Select(m => m.DocumentId)
            .ToListAsync();

        foreach (var docId in docIds)
        {
            var acl = await _db.DocumentAcls
                .FirstOrDefaultAsync(a => a.DocumentId == docId && a.UserId == req.UserId);

            if (acl != null)
            {
                acl.Permission = req.Permission;
                acl.ExpiresAt  = req.ExpiresAt;
                acl.GrantedAt  = DateTime.UtcNow;
                acl.GrantedBy  = adminId.Value;
            }
            else
            {
                _db.DocumentAcls.Add(new DocumentAcl
                {
                    Id         = Guid.NewGuid(),
                    DocumentId = docId,
                    UserId     = req.UserId,
                    Permission = req.Permission,
                    GrantedAt  = DateTime.UtcNow,
                    GrantedBy  = adminId.Value,
                    ExpiresAt  = req.ExpiresAt
                });
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Admin {Admin} assigned group '{Group}' to user {User} ({Permission}, expires: {Expires})",
            adminId, group.Name, req.UserId, req.Permission,
            req.ExpiresAt?.ToString("yyyy-MM-dd") ?? "never");

        return Ok(new GroupAssignmentDto
        {
            AssignmentId = existing.Id,
            UserId       = req.UserId,
            Username     = user.Username,
            FullName     = user.FullName,
            UserRole     = user.Role,
            Permission   = req.Permission,
            AssignedAt   = existing.AssignedAt,
            ExpiresAt    = req.ExpiresAt
        });
    }

    // ── Revoke group assignment ───────────────────────────────────────────────

    [HttpDelete("{id:guid}/assignments/{assignmentId:guid}")]
    public async Task<IActionResult> RevokeAssignment(Guid id, Guid assignmentId)
    {
        var assignment = await _db.UserGroupAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.GroupId == id);

        if (assignment == null) return NotFound();

        // Remove expanded ACL rows that came from this group assignment
        var docIds = await _db.DocumentGroupMembers
            .Where(m => m.GroupId == id)
            .Select(m => m.DocumentId)
            .ToListAsync();

        var aclsToRemove = await _db.DocumentAcls
            .Where(a => a.UserId == assignment.UserId && docIds.Contains(a.DocumentId))
            .ToListAsync();

        _db.DocumentAcls.RemoveRange(aclsToRemove);
        _db.UserGroupAssignments.Remove(assignment);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Admin {Admin} revoked group assignment {AssignmentId} (group {GroupId}, user {UserId})",
            GetCurrentUserId(), assignmentId, id, assignment.UserId);

        return Ok(new { message = "Assignment revoked." });
    }

    // ── All users access summary ───────────────────────────────────────────────

    [HttpGet("users/access-summary")]
    public async Task<ActionResult<List<UserAccessSummaryDto>>> GetAllUsersAccessSummary()
    {
        var allUsers = _authService.GetAllUsers();

        var assignments = await _db.UserGroupAssignments.ToListAsync();
        var groups = await _db.DocumentGroups
            .Where(g => g.IsActive)
            .ToListAsync();
        var groupLookup = groups.ToDictionary(g => g.Id);

        var docCounts = await _db.DocumentGroupMembers
            .GroupBy(m => m.GroupId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var directAcls = await _db.DocumentAcls.ToListAsync();
        var groupAssignedDocIds = await _db.DocumentGroupMembers
            .Select(m => new { m.GroupId, m.DocumentId })
            .ToListAsync();

        var result = allUsers.Select(user =>
        {
            var userAssignments = assignments.Where(a => a.UserId == user.Id).ToList();

            // Direct ACL grants = grants NOT covered by any group assignment for this user
            var groupDocIds = groupAssignedDocIds
                .Where(x => userAssignments.Any(a => a.GroupId == x.GroupId))
                .Select(x => x.DocumentId)
                .ToHashSet();

            var directGrants = directAcls
                .Where(a => a.UserId == user.Id && !groupDocIds.Contains(a.DocumentId))
                .Select(a => new DocumentAclDto
                {
                    Id         = a.Id,
                    DocumentId = a.DocumentId,
                    UserId     = a.UserId,
                    Username   = user.Username,
                    FullName   = user.FullName,
                    Permission = a.Permission,
                    GrantedAt  = a.GrantedAt,
                    ExpiresAt  = a.ExpiresAt,
                    IsActive   = a.IsActive
                }).ToList();

            return new UserAccessSummaryDto
            {
                UserId   = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Role     = user.Role,
                IsActive = user.IsActive,
                Groups   = userAssignments.Select(a =>
                {
                    groupLookup.TryGetValue(a.GroupId, out var g);
                    return new UserGroupSummary
                    {
                        AssignmentId  = a.Id,
                        GroupId       = a.GroupId,
                        GroupName     = g?.Name ?? "(deleted)",
                        GroupColor    = g?.Color,
                        GroupIcon     = g?.Icon,
                        DocumentCount = docCounts.GetValueOrDefault(a.GroupId, 0),
                        Permission    = a.Permission,
                        AssignedAt    = a.AssignedAt,
                        ExpiresAt     = a.ExpiresAt
                    };
                }).ToList(),
                DirectGrants = directGrants
            };
        }).ToList();

        return Ok(result);
    }

    // ── Single user access ────────────────────────────────────────────────────

    [HttpGet("users/{userId:guid}/access")]
    public async Task<ActionResult<UserAccessSummaryDto>> GetUserAccess(Guid userId)
    {
        var user = _authService.GetUserById(userId);
        if (user == null) return NotFound();

        var assignments = await _db.UserGroupAssignments
            .Where(a => a.UserId == userId)
            .ToListAsync();

        var groups = await _db.DocumentGroups.Where(g => g.IsActive).ToListAsync();
        var groupLookup = groups.ToDictionary(g => g.Id);

        var docCounts = await _db.DocumentGroupMembers
            .GroupBy(m => m.GroupId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var allAcls = await _db.DocumentAcls.Where(a => a.UserId == userId).ToListAsync();

        var groupDocIds = await _db.DocumentGroupMembers
            .Where(m => assignments.Select(a => a.GroupId).Contains(m.GroupId))
            .Select(m => m.DocumentId)
            .ToListAsync();

        var directGrants = allAcls
            .Where(a => !groupDocIds.Contains(a.DocumentId))
            .Select(a => new DocumentAclDto
            {
                Id         = a.Id,
                DocumentId = a.DocumentId,
                UserId     = a.UserId,
                Username   = user.Username,
                FullName   = user.FullName,
                Permission = a.Permission,
                GrantedAt  = a.GrantedAt,
                ExpiresAt  = a.ExpiresAt,
                IsActive   = a.IsActive
            }).ToList();

        return Ok(new UserAccessSummaryDto
        {
            UserId   = user.Id,
            Username = user.Username,
            FullName = user.FullName,
            Role     = user.Role,
            IsActive = user.IsActive,
            Groups = assignments.Select(a =>
            {
                groupLookup.TryGetValue(a.GroupId, out var g);
                return new UserGroupSummary
                {
                    AssignmentId  = a.Id,
                    GroupId       = a.GroupId,
                    GroupName     = g?.Name ?? "(deleted)",
                    GroupColor    = g?.Color,
                    GroupIcon     = g?.Icon,
                    DocumentCount = docCounts.GetValueOrDefault(a.GroupId, 0),
                    Permission    = a.Permission,
                    AssignedAt    = a.AssignedAt,
                    ExpiresAt     = a.ExpiresAt
                };
            }).ToList(),
            DirectGrants = directGrants
        });
    }

    // ── Change user role ──────────────────────────────────────────────────────
    // Also exposed here for convenience alongside group management.
    // (AuthController also has a PUT /api/auth/users/{id} that can do this)

    [HttpPatch("users/{userId:guid}/role")]
    public async Task<IActionResult> ChangeUserRole(Guid userId, [FromBody] ChangeRoleRequest req)
    {
        var adminId = GetCurrentUserId();
        if (adminId == null) return Unauthorized();

        // Prevent an admin from accidentally demoting themselves
        if (userId == adminId && req.Role != UserRole.Admin)
            return BadRequest(new { error = "You cannot change your own role." });

        var user = _authService.GetUserById(userId);
        if (user == null) return NotFound();

        var updateReq = new RegisterRequest
        {
            Username          = user.Username,
            Password          = "",        // empty = no password change
            FullName          = user.FullName,
            Email             = user.Email,
            Role              = req.Role,
            AllowedCategories = user.AllowedCategories
        };

        var (success, error) = _authService.UpdateUser(userId, updateReq);
        if (!success) return BadRequest(new { error });

        _logger.LogInformation(
            "Admin {Admin} changed role of user {User} to {Role}",
            adminId, userId, req.Role);

        return Ok(new { message = $"Role updated to {req.Role}." });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    /// <summary>
    /// When new documents are added to a group, propagate ACL rows to all
    /// currently-assigned users so they immediately get access.
    /// </summary>
    private async Task PropagateNewDocumentsToAssignments(
        Guid groupId,
        List<Guid> newDocIds,
        Guid adminId)
    {
        var assignments = await _db.UserGroupAssignments
            .Where(a => a.GroupId == groupId
                     && (a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow))
            .ToListAsync();

        foreach (var assignment in assignments)
        {
            foreach (var docId in newDocIds)
            {
                var existing = await _db.DocumentAcls
                    .FirstOrDefaultAsync(a => a.DocumentId == docId && a.UserId == assignment.UserId);

                if (existing == null)
                {
                    _db.DocumentAcls.Add(new DocumentAcl
                    {
                        Id         = Guid.NewGuid(),
                        DocumentId = docId,
                        UserId     = assignment.UserId,
                        Permission = assignment.Permission,
                        GrantedAt  = DateTime.UtcNow,
                        GrantedBy  = adminId,
                        ExpiresAt  = assignment.ExpiresAt
                    });
                }
            }
        }

        await _db.SaveChangesAsync();
    }
}
using System.ComponentModel.DataAnnotations;

namespace GED.Core.Models;

// ── Document Group ─────────────────────────────────────────────────────────────

/// <summary>
/// A named bundle of documents that can be granted to users/managers as a unit.
/// Instead of granting document-by-document, the admin creates a group
/// (e.g. "RH - Contracts 2024"), adds documents to it, then assigns the group
/// to one or more users with a chosen permission level.
/// </summary>
public class DocumentGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Visual color tag (hex, e.g. "#2563eb") to distinguish groups in the UI.
    /// </summary>
    [MaxLength(20)]
    public string? Color { get; set; }

    /// <summary>
    /// Optional icon/emoji for the group (e.g. "📁", "🔒", "📊").
    /// </summary>
    [MaxLength(10)]
    public string? Icon { get; set; }

    /// <summary>
    /// Optional category label (e.g. "RH", "Finance", "Juridique").
    /// </summary>
    [MaxLength(100)]
    public string? Category { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A document belonging to a group. Many-to-many pivot.
/// </summary>
public class DocumentGroupMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public Guid DocumentId { get; set; }

    /// <summary>Default permission applied when this group is assigned to a user.</summary>
    public AclPermission DefaultPermission { get; set; } = AclPermission.Read;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public Guid AddedBy { get; set; }
}

/// <summary>
/// Assignment of a document group to a user.
/// When created, the system expands it into individual DocumentAcl rows
/// so the existing ACL enforcement layer is unchanged.
/// </summary>
public class UserGroupAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }

    public AclPermission Permission { get; set; } = AclPermission.Read;

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public Guid AssignedBy { get; set; }

    /// <summary>Optional expiry — propagated to DocumentAcl rows.</summary>
    public DateTime? ExpiresAt { get; set; }
}

// ── DTOs ───────────────────────────────────────────────────────────────────────

public class CreateGroupRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(20)]
    public string? Color { get; set; }

    [MaxLength(10)]
    public string? Icon { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>Optional list of document IDs to add immediately on creation.</summary>
    public List<Guid>? DocumentIds { get; set; }
}

public class UpdateGroupRequest
{
    [MaxLength(200)]
    public string? Name { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(20)]
    public string? Color { get; set; }

    [MaxLength(10)]
    public string? Icon { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }
}

public class AddDocumentsToGroupRequest
{
    [Required]
    public List<Guid> DocumentIds { get; set; } = new();

    public AclPermission DefaultPermission { get; set; } = AclPermission.Read;
}

public class AssignGroupToUserRequest
{
    [Required]
    public Guid UserId { get; set; }

    public AclPermission Permission { get; set; } = AclPermission.Read;

    public DateTime? ExpiresAt { get; set; }
}

public class DocumentGroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public string? Category { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public int DocumentCount { get; set; }
    public int UserCount { get; set; }

    /// <summary>Documents inside the group (populated when fetching a single group).</summary>
    public List<GroupDocumentDto>? Documents { get; set; }

    /// <summary>Users assigned to this group (populated when fetching a single group).</summary>
    public List<GroupAssignmentDto>? Assignments { get; set; }
}

public class GroupDocumentDto
{
    public Guid MemberId { get; set; }
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public AclPermission DefaultPermission { get; set; }
    public DateTime AddedAt { get; set; }
}

public class GroupAssignmentDto
{
    public Guid AssignmentId { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public UserRole UserRole { get; set; }
    public AclPermission Permission { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive => ExpiresAt == null || ExpiresAt > DateTime.UtcNow;
}

/// <summary>Summary of all groups a user has been assigned, including document count.</summary>
public class UserAccessSummaryDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Groups assigned to this user.</summary>
    public List<UserGroupSummary> Groups { get; set; } = new();

    /// <summary>Direct (non-group) ACL grants.</summary>
    public List<DocumentAclDto> DirectGrants { get; set; } = new();
}

public class UserGroupSummary
{
    public Guid AssignmentId { get; set; }
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string? GroupColor { get; set; }
    public string? GroupIcon { get; set; }
    public int DocumentCount { get; set; }
    public AclPermission Permission { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>Request to change a user's system role (Admin / Manager / User / ReadOnly).</summary>
public class ChangeRoleRequest
{
    [Required]
    public UserRole Role { get; set; }
}
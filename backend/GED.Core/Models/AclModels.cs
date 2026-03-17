using System.ComponentModel.DataAnnotations;

namespace GED.Core.Models;

// ── User ──────────────────────────────────────────────────────────────────────

public class AppUser
{
    public Guid Id { get; set; }

    [Required, MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? FullName { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    /// <summary>Role assigned to this user (Admin, Manager, User, ReadOnly).</summary>
    public UserRole Role { get; set; } = UserRole.User;

    /// <summary>
    /// Category-level permissions: which document categories this user can see.
    /// Empty = access to all categories.
    /// </summary>
    public List<string>? AllowedCategories { get; set; }
}

public enum UserRole
{
    /// <summary>Full access: create/read/update/delete all documents, manage users.</summary>
    Admin,

    /// <summary>Can upload, read, update documents. Cannot manage users.</summary>
    Manager,

    /// <summary>Can upload and read documents. Cannot delete or update others'.</summary>
    User,

    /// <summary>Read-only access to permitted documents.</summary>
    ReadOnly
}

// ── Auth DTOs ─────────────────────────────────────────────────────────────────

public class LoginRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public Guid   UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public UserRole Role { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class RegisterRequest
{
    [Required, MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? FullName { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    public List<string>? AllowedCategories { get; set; }
}

public class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public List<string>? AllowedCategories { get; set; }
}

// ── Document ACL ──────────────────────────────────────────────────────────────

/// <summary>
/// Per-document access control entry.
/// When no entries exist for a document, it inherits the user's role-level access.
/// Supports both permanent access (ExpiresAt = null) and time-limited access.
/// </summary>
public class DocumentAcl
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid UserId { get; set; }
    public AclPermission Permission { get; set; }
    public DateTime GrantedAt { get; set; }
    public Guid GrantedBy { get; set; }

    /// <summary>
    /// Optional expiry date for time-limited access.
    /// null = permanent access.
    /// If set and in the past, the ACL entry is treated as revoked.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Computed: whether this ACL entry is currently active.</summary>
    public bool IsActive => ExpiresAt == null || ExpiresAt > DateTime.UtcNow;
}

public enum AclPermission
{
    Read,
    Write,
    Delete,
    FullControl
}

// ── ACL Request DTOs ──────────────────────────────────────────────────────────

/// <summary>Request to grant document access to a user.</summary>
public class GrantAccessRequest
{
    [Required]
    public Guid UserId { get; set; }

    public AclPermission Permission { get; set; } = AclPermission.Read;

    /// <summary>
    /// Optional: how long to grant access for.
    /// null = permanent. Set a future date for time-limited access.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>Response DTO showing a user's access to a document.</summary>
public class DocumentAclDto
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public AclPermission Permission { get; set; }
    public DateTime GrantedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public bool IsPermanent => ExpiresAt == null;
}
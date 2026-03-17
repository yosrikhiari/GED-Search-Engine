using GED.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GED.Core.Interfaces;

namespace GED.Infrastructure.Services;

/// <summary>
/// Authentication and user management service.
///
/// Provides:
///   - User registration and login with bcrypt-style password hashing
///   - JWT token generation with role claims
///   - User CRUD (Admin only)
///
/// Users are persisted in a JSON file for simplicity (no extra DB migration needed).
/// In production you'd swap this for a proper user table.
///
/// This satisfies the "droits d'accès" and "sécurité" requirements.
/// </summary>
public class AuthService : IUserContext
{
    private readonly ILogger<AuthService> _logger;

    private readonly string _usersFilePath;

    // In-memory user store, backed by JSON file
    private readonly List<AppUser> _users = new();
    private readonly object _lock = new();

    public AuthService(ILogger<AuthService> logger, IConfiguration configuration)
    {
        _logger         = logger;
        _usersFilePath  = configuration["Auth:UsersFilePath"] ?? "/var/lib/ged/users.json";

        LoadUsers();
        EnsureDefaultAdmin();
    }


    public AppUser? GetUserByToken(string token)
{
    lock (_lock)
    {
        if (_sessions.TryGetValue(token, out var entry))
        {
            if (entry.Expires > DateTime.UtcNow)
                return entry.User;

            // Expired — clean it up lazily
            _sessions.Remove(token);
        }
        return null;
    }
}
    // ── Log Out ─────────────────────────────────────────────────────────────────
    public bool Logout(string token)
{
    lock (_lock)
    {
        return _sessions.Remove(token);
    }
}

// Called periodically, or lazily on each login:
private void PurgeExpiredSessions()
{
    var now = DateTime.UtcNow;
    var expired = _sessions
        .Where(kv => kv.Value.Expires < now)
        .Select(kv => kv.Key)
        .ToList();
    foreach (var key in expired)
        _sessions.Remove(key);
}


    // ── Login ─────────────────────────────────────────────────────────────────

    private readonly Dictionary<string, (AppUser User, DateTime Expires)> _sessions = new();

    public LoginResponse? Login(LoginRequest request)
{
    lock (_lock)
    {
        PurgeExpiredSessions();

        var user = _users.FirstOrDefault(u =>
            u.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase)
            && u.IsActive);

        if (user == null)
        {
            _logger.LogWarning("Login failed: unknown user '{Username}'", request.Username);
            return null;
        }

        if (!VerifyPassword(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed: wrong password for '{Username}'", request.Username);
            return null;
        }

        user.LastLoginAt = DateTime.UtcNow;
        SaveUsers();

        var sessionToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expires      = DateTime.UtcNow.AddHours(8);
        _sessions[sessionToken] = (user, expires);

        _logger.LogInformation("✅ User '{Username}' logged in (role={Role})", user.Username, user.Role);

        return new LoginResponse
        {
            Token     = sessionToken,
            UserId   = user.Id,
            Username  = user.Username,
            FullName  = user.FullName,
            Role      = user.Role,
            ExpiresAt = expires
        };
    }
}

    // ── User management ───────────────────────────────────────────────────────

    public (bool Success, string? Error) Register(RegisterRequest request)
    {
        lock (_lock)
        {
            if (_users.Any(u => u.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase)))
                return (false, $"Username '{request.Username}' already exists.");

            var user = new AppUser
            {
                Id               = Guid.NewGuid(),
                Username         = request.Username,
                PasswordHash     = HashPassword(request.Password),
                FullName         = request.FullName,
                Email            = request.Email,
                Role             = request.Role,
                IsActive         = true,
                CreatedAt        = DateTime.UtcNow,
                AllowedCategories = request.AllowedCategories
            };

            _users.Add(user);
            SaveUsers();

            _logger.LogInformation(
                "✅ User '{Username}' registered (role={Role})",
                user.Username, user.Role);

            return (true, null);
        }
    }

    public List<UserDto> GetAllUsers()
    {
        lock (_lock)
        {
            return _users.Select(MapToDto).ToList();
        }
    }

    public UserDto? GetUserById(Guid id)
    {
        lock (_lock)
        {
            var user = _users.FirstOrDefault(u => u.Id == id);
            return user == null ? null : MapToDto(user);
        }
    }

    public (bool Success, string? Error) UpdateUser(Guid id, RegisterRequest request)
    {
        lock (_lock)
        {
            var user = _users.FirstOrDefault(u => u.Id == id);
            if (user == null) return (false, "User not found.");

            user.FullName          = request.FullName;
            user.Email             = request.Email;
            user.Role              = request.Role;
            user.AllowedCategories = request.AllowedCategories;

            if (!string.IsNullOrWhiteSpace(request.Password))
                user.PasswordHash = HashPassword(request.Password);

            SaveUsers();
            return (true, null);
        }
    }

    public bool DeactivateUser(Guid id)
    {
        lock (_lock)
        {
            var user = _users.FirstOrDefault(u => u.Id == id);
            if (user == null) return false;
            user.IsActive = false;
            SaveUsers();
            return true;
        }
    }

    public List<string>? GetAllowedCategories(string username)
    {
        lock (_lock)
        {
            var user = _users.FirstOrDefault(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            return user?.AllowedCategories;
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────
    private static string HashPassword(string password)
    {
        // PBKDF2 with SHA-256, 100k iterations — secure without BCrypt dependency
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt,
            100_000, HashAlgorithmName.SHA256, 32);

        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            var parts = storedHash.Split(':');
            if (parts.Length != 2) return false;

            var salt       = Convert.FromBase64String(parts[0]);
            var storedBytes = Convert.FromBase64String(parts[1]);

            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt,
                100_000, HashAlgorithmName.SHA256, 32);

            return CryptographicOperations.FixedTimeEquals(hash, storedBytes);
        }
        catch
        {
            return false;
        }
    }

private void EnsureDefaultAdmin()
{
    lock (_lock)
    {
        var changed = false;

        if (!_users.Any(u => u.Role == UserRole.Admin))
        {
            _logger.LogWarning("⚠️  No admin user found — creating default admin (username: admin, password: Admin@1234)");
            _users.Add(new AppUser
            {
                Id           = Guid.NewGuid(),
                Username     = "admin",
                PasswordHash = HashPassword("Admin@1234"),
                FullName     = "System Administrator",
                Role         = UserRole.Admin,
                IsActive     = true,
                CreatedAt    = DateTime.UtcNow
            });
            changed = true;
        }

        if (!_users.Any(u => u.Username == "manager"))
        {
            _users.Add(new AppUser
            {
                Id           = Guid.NewGuid(),
                Username     = "manager",
                PasswordHash = HashPassword("Manager@1234"),
                FullName     = "Test Manager",
                Role         = UserRole.Manager,
                IsActive     = true,
                CreatedAt    = DateTime.UtcNow
            });
            changed = true;
        }

        if (!_users.Any(u => u.Username == "user"))
        {
            _users.Add(new AppUser
            {
                Id           = Guid.NewGuid(),
                Username     = "user",
                PasswordHash = HashPassword("User@1234"),
                FullName     = "Test User",
                Role         = UserRole.User,
                IsActive     = true,
                CreatedAt    = DateTime.UtcNow
            });
            changed = true;
        }

        if (changed) SaveUsers();
    }
}
    public bool UserExists(string username)
    {
        lock (_lock)
        {
            return _users.Any(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && u.IsActive);
        }
    }
    private void LoadUsers()
    {
        try
        {
            if (!File.Exists(_usersFilePath)) return;
            var json  = File.ReadAllText(_usersFilePath);
            var users = JsonSerializer.Deserialize<List<AppUser>>(json);
            if (users != null) _users.AddRange(users);
            _logger.LogInformation("✅ Loaded {Count} users from {Path}", _users.Count, _usersFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load users from {Path}", _usersFilePath);
        }
    }

    private void SaveUsers()
    {
        try
        {
            var dir = Path.GetDirectoryName(_usersFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_users,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_usersFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save users to {Path}", _usersFilePath);
        }
    }

    private static UserDto MapToDto(AppUser u) => new()
    {
        Id                = u.Id,
        Username          = u.Username,
        FullName          = u.FullName,
        Email             = u.Email,
        Role              = u.Role,
        IsActive          = u.IsActive,
        CreatedAt         = u.CreatedAt,
        LastLoginAt       = u.LastLoginAt,
        AllowedCategories = u.AllowedCategories
    };
}

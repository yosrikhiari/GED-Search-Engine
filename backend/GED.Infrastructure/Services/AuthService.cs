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
///   - Session-based authentication (cookie) with role claims
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

    /// <summary>
    /// In-memory user store, backed by JSON file for persistence.
    /// Thread-safety ensured via <see cref="_lock"/>.
    /// </summary>
    private readonly List<AppUser> _users = new();

    /// <summary>
    /// Lock object for thread-safe access to session and user data.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// Active user sessions keyed by session token.
    /// Sessions auto-expire after 8 hours and are lazily cleaned up.
    /// </summary>
    private readonly Dictionary<string, (AppUser User, DateTime Expires)> _sessions = new();

    /// <summary>
    /// Username lookup index for O(1) access.
    /// Maintained in sync with <see cref="_users"/> list.
    /// </summary>
    private Dictionary<string, AppUser> _usersByUsername = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of <see cref="AuthService"/>.
    /// Loads existing users from disk and ensures default admin/manager/user accounts exist.
    /// </summary>
    /// <param name="logger">Logger for authentication events.</param>
    /// <param name="configuration">Application configuration containing Auth:UsersFilePath setting.</param>
    public AuthService(ILogger<AuthService> logger, IConfiguration configuration)
    {
        _logger         = logger;
        _usersFilePath  = configuration["Auth:UsersFilePath"] ?? "/var/lib/ged/users.json";

        LoadUsers();
        EnsureDefaultAdmin();
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public bool Logout(string token)
    {
        lock (_lock)
        {
            return _sessions.Remove(token);
        }
    }

    /// <summary>
    /// Purges all expired sessions from the in-memory session store.
    /// Called periodically and lazily on each login attempt.
    /// </summary>
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

    /// <inheritdoc />
    public LoginResponse? Login(LoginRequest request)
    {
        lock (_lock)
        {
            PurgeExpiredSessions();

            // Find active user by username (case-insensitive)
            var user = _users.FirstOrDefault(u =>
                u.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase)
                && u.IsActive);

            if (user == null)
            {
                _logger.LogWarning("Login failed: unknown user '{Username}'", request.Username);
                return null;
            }

            // Verify password using constant-time comparison to prevent timing attacks
            if (!VerifyPassword(request.Password, user.PasswordHash))
            {
                _logger.LogWarning("Login failed: wrong password for '{Username}'", request.Username);
                return null;
            }

            user.LastLoginAt = DateTime.UtcNow;
            SaveUsers();

            // Generate cryptographically secure session token (256 bits)
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

    /// <inheritdoc />
    public (bool Success, string? Error) Register(RegisterRequest request)
    {
        lock (_lock)
        {
            // Check for duplicate username (case-insensitive)
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
            // Maintain username index for O(1) lookup
            _usersByUsername[user.Username.ToLowerInvariant()] = user;
            SaveUsers();

            _logger.LogInformation(
                "✅ User '{Username}' registered (role={Role})",
                user.Username, user.Role);

            return (true, null);
        }
    }

    /// <inheritdoc />
    public List<UserDto> GetAllUsers()
    {
        lock (_lock)
        {
            return _users.Select(MapToDto).ToList();
        }
    }

    /// <inheritdoc />
    public UserDto? GetUserById(Guid id)
    {
        lock (_lock)
        {
            var user = _users.FirstOrDefault(u => u.Id == id);
            return user == null ? null : MapToDto(user);
        }
    }

    /// <inheritdoc />
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

            // Only update password if provided (allows partial updates)
            if (!string.IsNullOrWhiteSpace(request.Password))
                user.PasswordHash = HashPassword(request.Password);

            SaveUsers();
            return (true, null);
        }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public List<string>? GetAllowedCategories(string username)
    {
        lock (_lock)
        {
            var user = GetUserByUsernameInternal(username);
            return user?.AllowedCategories;
        }
    }

    /// <summary>
    /// Fast O(1) lookup by username using case-insensitive comparison.
    /// </summary>
    /// <param name="username">The username to look up.</param>
    /// <returns>User DTO if found, null otherwise.</returns>
    public UserDto? GetUserByUsername(string username)
    {
        lock (_lock)
        {
            var user = GetUserByUsernameInternal(username);
            return user == null ? null : MapToDto(user);
        }
    }

    /// <summary>
    /// Internal method for fast username lookup using the username index.
    /// </summary>
    private AppUser? GetUserByUsernameInternal(string username)
    {
        var key = username.ToLowerInvariant();
        return _usersByUsername.TryGetValue(key, out var user) ? user : null;
    }

    /// <inheritdoc />
    public bool UserExists(string username)
    {
        lock (_lock)
        {
            return _users.Any(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && u.IsActive);
        }
    }

    /// <summary>
    /// Hashes a password using PBKDF2 with SHA-256.
    /// Uses 100,000 iterations for security against brute-force attacks.
    /// </summary>
    /// <param name="password">The plaintext password to hash.</param>
    /// <returns>Base64-encoded salt and hash in format "salt:hash".</returns>
    private static string HashPassword(string password)
    {
        // PBKDF2 with SHA-256, 100k iterations — secure without BCrypt dependency
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt,
            100_000, HashAlgorithmName.SHA256, 32);

        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Verifies a password against a stored hash using constant-time comparison.
    /// </summary>
    /// <param name="password">The plaintext password to verify.</param>
    /// <param name="storedHash">The stored hash in "salt:hash" format.</param>
    /// <returns>True if password matches, false otherwise.</returns>
    private static bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            var parts = storedHash.Split(':');
            if (parts.Length != 2) return false;

            var salt       = Convert.FromBase64String(parts[0]);
            var storedBytes = Convert.FromBase64String(parts[1]);

            // Re-compute hash with same salt and compare using constant-time algorithm
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

    /// <summary>
    /// Ensures default system accounts exist (admin, manager, user).
    /// Creates them with known default passwords if they don't exist.
    /// </summary>
    private void EnsureDefaultAdmin()
    {
        lock (_lock)
        {
            var changed = false;

            // Create admin account if no admin exists
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

            // Create manager account for testing
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

            // Create regular user account for testing
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

    /// <summary>
    /// Loads users from the JSON file into memory.
    /// Builds the username index for fast lookups.
    /// </summary>
    private void LoadUsers()
    {
        try
        {
            if (!File.Exists(_usersFilePath)) return;
            var json  = File.ReadAllText(_usersFilePath);
            var users = JsonSerializer.Deserialize<List<AppUser>>(json);
            if (users != null)
            {
                _users.AddRange(users);
                // Build username index for O(1) lookup
                foreach (var u in users)
                {
                    _usersByUsername[u.Username.ToLowerInvariant()] = u;
                }
            }
            _logger.LogInformation("✅ Loaded {Count} users from {Path}", _users.Count, _usersFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load users from {Path}", _usersFilePath);
        }
    }

    /// <summary>
    /// Persists the in-memory user list to the JSON file.
    /// Creates the directory if it doesn't exist.
    /// </summary>
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

    /// <summary>
    /// Maps an <see cref="AppUser"/> entity to a <see cref="UserDto"/>.
    /// </summary>
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

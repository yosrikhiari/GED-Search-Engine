using GED.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using GED.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GED.Core.Interfaces;

namespace GED.Infrastructure.Services;

/// <summary>
/// Authentication and user management service.
/// 
/// Provides:
///   - User registration and login with PBKDF2 password hashing
///   - Session-based authentication (cookie) with role claims
///   - User CRUD (Admin only)
///
/// Users are persisted in a JSON file for simplicity (no extra DB migration needed).
/// In production you'd swap this for a proper user table.
///
/// Sessions are stored in Redis (via IDistributedCache) for horizontal scalability,
/// with in-memory fallback if Redis is unavailable.
/// </summary>
public class AuthService : IUserContext, IAuthService
{
    private readonly ILogger<AuthService> _logger;
    private readonly IDbContextFactory<GedDbContext>? _dbFactory;
    private readonly string? _usersFilePath;
    private readonly IDistributedCache? _cache;
    private readonly TimeSpan _sessionDuration;
    private readonly bool _useRedis;
    private readonly bool _useDatabase;
    private bool _initialized;

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
    /// Only used for in-memory fallback; Redis is primary storage when available.
    /// Sessions auto-expire after 8 hours and are lazily cleaned up.
    /// </summary>
    private readonly Dictionary<string, (AppUser User, DateTime Expires)> _sessions = new();

    /// <summary>
    /// Username lookup index for O(1) access.
    /// Maintained in sync with <see cref="_users"/> list.
    /// </summary>
    private Dictionary<string, AppUser> _usersByUsername = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tracks failed login attempts per brute force key (username:ip) for brute force protection.
    /// NOTE: This is in-memory only. In multi-instance deployments, lockout state
    /// is not shared across instances and will reset on application restart.
    /// For distributed environments, replace with a Redis-backed implementation.
    /// Lockout is keyed by username:ip to prevent attackers from bypassing by rotating IPs.
    /// </summary>
    private readonly Dictionary<string, (int Attempts, DateTime? LockoutUntil)> _failedAttempts = new();

    /// <summary>
    /// Number of failed attempts before lockout.
    /// </summary>
    private const int MaxFailedAttempts = 5;

    /// <summary>
    /// Duration of lockout after exceeding max failed attempts.
    /// </summary>
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Initializes a new instance of <see cref="AuthService"/> with Redis session support.
    /// Note: Initialization is deferred to InitializeAsync() to avoid file I/O in constructor.
    /// </summary>
    /// <param name="logger">Logger for authentication events.</param>
    /// <param name="configuration">Application configuration containing Auth:UsersFilePath setting.</param>
    /// <param name="cache">Optional Redis cache for session storage. If null, uses in-memory sessions.</param>
    /// <param name="dbFactory">Optional DbContextFactory for database-backed user storage.</param>
    public AuthService(
        ILogger<AuthService> logger, 
        IConfiguration configuration,
        IDistributedCache? cache = null,
        IDbContextFactory<GedDbContext>? dbFactory = null)
    {
        _logger         = logger;
        _dbFactory      = dbFactory;
        _usersFilePath  = configuration["Auth:UsersFilePath"];
        _cache          = cache;
        _useRedis       = cache != null;
        _useDatabase    = dbFactory != null;
        
        var sessionHours = configuration["Auth:SessionDurationHours"];
        _sessionDuration = TimeSpan.FromHours(int.TryParse(sessionHours, out var h) ? h : 8);

        if (_useRedis)
        {
            _logger.LogInformation("Using Redis-backed session storage");
        }
        else
        {
            _logger.LogWarning("Redis not available - using in-memory session storage");
        }

        if (string.IsNullOrWhiteSpace(_usersFilePath) && !_useDatabase)
        {
            _logger.LogWarning("No user persistence configured: both database and file path are unavailable. Users will exist only in memory.");
        }
        // Note: LoadUsers() and EnsureDefaultAdmin() are now called via InitializeAsync()
    }

    /// <summary>
    /// Initializes the AuthService by loading users from database or file.
    /// Call this once at application startup via AuthInitializationHostedService.
    /// </summary>
    public Task InitializeAsync()
    {
        if (_initialized)
        {
            _logger.LogDebug("AuthService already initialized, skipping");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Initializing AuthService...");
        LoadUsers();
        EnsureDefaultAdmin();
        _initialized = true;
        _logger.LogInformation("AuthService initialized with {UserCount} users", _users.Count);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public AppUser? GetUserByToken(string token)
    {
        if (_useRedis && _cache != null)
        {
            return GetUserByTokenRedis(token);
        }

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

    private AppUser? GetUserByTokenRedis(string token)
    {
        try
        {
            var cacheKey = $"ged:session:{token}";
            var cached = _cache?.GetString(cacheKey);
            if (string.IsNullOrEmpty(cached))
                return null;

            var session = JsonSerializer.Deserialize<SessionData>(cached);
            if (session == null || session.Expires < DateTime.UtcNow)
            {
                // Expired - remove from cache
                if (session != null)
                {
                    _cache?.Remove(cacheKey);
                }
                return null;
            }

            // Find user by ID
            var user = _users.FirstOrDefault(u => u.Id == session.UserId);
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading session from Redis - falling back to in-memory");
            // Fall back to in-memory on error
            lock (_lock)
            {
                if (_sessions.TryGetValue(token, out var entry))
                {
                    if (entry.Expires > DateTime.UtcNow)
                        return entry.User;
                    _sessions.Remove(token);
                }
                return null;
            }
        }
    }

    /// <inheritdoc />
    public bool Logout(string token)
    {
        if (_useRedis && _cache != null)
        {
            return LogoutRedis(token);
        }

        lock (_lock)
        {
            return _sessions.Remove(token);
        }
    }

    private bool LogoutRedis(string token)
    {
        try
        {
            var cacheKey = $"ged:session:{token}";
            _cache?.Remove(cacheKey);
            _logger.LogDebug("Session {Token} removed from Redis", token[..Math.Min(8, token.Length)]);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error removing session from Redis");
            lock (_lock)
            {
                return _sessions.Remove(token);
            }
        }
    }

    /// <summary>
    /// Purges all expired sessions from the in-memory session store.
    /// Called periodically and lazily on each login attempt.
    /// Note: Redis handles expiration automatically via TTL.
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

    /// <summary>
    /// Increments the failed login attempt counter for a brute force key (username:ip).
    /// After MaxFailedAttempts, sets a lockout period.
    /// </summary>
    private void IncrementFailedAttempts(string bfKey)
    {
        if (!_failedAttempts.TryGetValue(bfKey, out var info))
        {
            info = (0, null);
        }

        info.Attempts++;
        
        if (info.Attempts >= MaxFailedAttempts && info.LockoutUntil == null)
        {
            info.LockoutUntil = DateTime.UtcNow.Add(LockoutDuration);
            _logger.LogWarning(
                "Account locked for brute force key '{BfKey}' due to {Attempts} failed login attempts. Lockout until {LockoutUntil}",
                bfKey, info.Attempts, info.LockoutUntil);
        }
        
        _failedAttempts[bfKey] = info;
    }

    /// <summary>
    /// Session data stored in Redis.
    /// </summary>
    private class SessionData
    {
        public Guid UserId { get; set; }
        public DateTime Expires { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <inheritdoc />
    public LoginResponse? Login(LoginRequest request, string? clientIp = null)
    {
        var normalizedUsername = request.Username.ToLowerInvariant();
        var bfKey = string.IsNullOrEmpty(clientIp) 
            ? normalizedUsername 
            : $"{normalizedUsername}:{clientIp}";
        
        // Check for lockout due to brute force protection
        lock (_lock)
        {
            if (_failedAttempts.TryGetValue(bfKey, out var attemptInfo))
            {
                if (attemptInfo.LockoutUntil.HasValue && attemptInfo.LockoutUntil.Value > DateTime.UtcNow)
                {
                    _logger.LogWarning(
                        "Login blocked for '{Username}' from IP '{Ip}': brute force lockout until {LockoutUntil}",
                        request.Username, clientIp ?? "unknown", attemptInfo.LockoutUntil.Value);
                    // Do not reveal lockout reason in response
                    return null;
                }
                
                // Clean up expired lockout
                if (attemptInfo.LockoutUntil.HasValue && attemptInfo.LockoutUntil.Value <= DateTime.UtcNow)
                {
                    _failedAttempts.Remove(bfKey);
                }
            }
        }

        AppUser? user;
        
        // Find user (outside lock for Redis to minimize lock time)
        lock (_lock)
        {
            PurgeExpiredSessions();
            
            user = _users.FirstOrDefault(u =>
                u.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase)
                && u.IsActive);
        }

        if (user == null)
        {
            _logger.LogWarning("Login failed: unknown user '{Username}'", request.Username);
            return null;
        }

        // Verify password using constant-time comparison to prevent timing attacks
        if (!VerifyPassword(request.Password, user.PasswordHash))
        {
            lock (_lock)
            {
                IncrementFailedAttempts(bfKey);
            }
            _logger.LogWarning("Login failed: wrong password for '{Username}' from IP '{Ip}'", request.Username, clientIp ?? "unknown");
            return null;
        }

        // Successful login - reset failed attempts
        lock (_lock)
        {
            _failedAttempts.Remove(bfKey);
        }

        // Migrate password hash to new iteration count if needed
        var currentIterations = GetIterationsFromHash(user.PasswordHash);
        bool passwordHashMigrated = false;
        if (currentIterations < CurrentIterationCount)
        {
            _logger.LogInformation(
                "Migrating password hash for user '{Username}' from {OldIter} to {NewIter} iterations",
                user.Username, currentIterations, CurrentIterationCount);
            
            user.PasswordHash = HashPassword(request.Password);
            passwordHashMigrated = true;
        }

        // Update last login time and save if password was migrated
        lock (_lock)
        {
            user.LastLoginAt = DateTime.UtcNow;
            if (passwordHashMigrated)
            {
                SaveUsers();
            }
        }

        // Generate cryptographically secure session token (256 bits)
        var sessionToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expires = DateTime.UtcNow.Add(_sessionDuration);

        // Store session in Redis or in-memory
        if (_useRedis && _cache != null)
        {
            try
            {
                var sessionData = new SessionData
                {
                    UserId = user.Id,
                    Expires = expires,
                    CreatedAt = DateTime.UtcNow
                };
                
                var cacheKey = $"ged:session:{sessionToken}";
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpiration = expires
                };
                
                _cache.SetString(cacheKey, JsonSerializer.Serialize(sessionData), options);
                _logger.LogInformation("Session stored in Redis for user '{Username}'", user.Username);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to store session in Redis - using in-memory fallback");
                lock (_lock)
                {
                    _sessions[sessionToken] = (user, expires);
                }
            }
        }
        else
        {
            lock (_lock)
            {
                _sessions[sessionToken] = (user, expires);
            }
        }

        _logger.LogInformation("User '{Username}' logged in (role={Role}, sessionStore={Store})", 
            user.Username, user.Role, _useRedis ? "Redis" : "Memory");

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
    /// Current PBKDF2 iteration count (OWASP 2023 recommended minimum: 310,000 for SHA-256).
    /// </summary>
    private const int CurrentIterationCount = 310_000;

    /// <summary>
    /// Hashes a password using PBKDF2 with SHA-256.
    /// Uses 310,000 iterations for security against brute-force attacks (OWASP 2023).
    /// Format: "iterations:salt:hash" to support future migrations.
    /// </summary>
    /// <param name="password">The plaintext password to hash.</param>
    /// <returns>Base64-encoded salt and hash in format "iterations:salt:hash".</returns>
    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt,
            CurrentIterationCount, HashAlgorithmName.SHA256, 32);

        return $"{CurrentIterationCount}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Parses the iteration count from a stored hash.
    /// Returns default for legacy hashes (format: "salt:hash" without iteration count).
    /// </summary>
    private static int GetIterationsFromHash(string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length == 3 && int.TryParse(parts[0], out var iterations))
        {
            return iterations;
        }
        return 100_000; // Legacy default
    }

    /// <summary>
    /// Verifies a password against a stored hash using constant-time comparison.
    /// Supports both legacy format ("salt:hash") and new format ("iterations:salt:hash").
    /// </summary>
    /// <param name="password">The plaintext password to verify.</param>
    /// <param name="storedHash">The stored hash in either format.</param>
    /// <returns>True if password matches, false otherwise.</returns>
    private static bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            var parts = storedHash.Split(':');
            int iterations;
            string saltBase64;
            string hashBase64;

            if (parts.Length == 3)
            {
                // New format: "iterations:salt:hash"
                iterations = int.Parse(parts[0]);
                saltBase64 = parts[1];
                hashBase64 = parts[2];
            }
            else if (parts.Length == 2)
            {
                // Legacy format: "salt:hash"
                iterations = 100_000;
                saltBase64 = parts[0];
                hashBase64 = parts[1];
            }
            else
            {
                return false;
            }

            var salt = Convert.FromBase64String(saltBase64);
            var storedBytes = Convert.FromBase64String(hashBase64);

            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt,
                iterations, HashAlgorithmName.SHA256, 32);

            return CryptographicOperations.FixedTimeEquals(hash, storedBytes);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ensures default system accounts exist (admin, manager, user).
    /// Creates them with passwords from environment variables (or generated if not set).
    /// Only creates default accounts in Development environment.
    /// </summary>
    private void EnsureDefaultAdmin()
    {
        var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        var createDefaults = Environment.GetEnvironmentVariable("AUTH_CREATE_DEFAULTS")?.ToLower() == "true";

        if (!isDevelopment && !createDefaults)
        {
            _logger.LogInformation("Skipping default user creation (not Development and AUTH_CREATE_DEFAULTS != true)");
            return;
        }

        lock (_lock)
        {
            var changed = false;

            var defaultAdminPassword = Environment.GetEnvironmentVariable("AUTH_DEFAULT_ADMIN_PASSWORD") ?? GenerateSecurePassword();
            var defaultManagerPassword = Environment.GetEnvironmentVariable("AUTH_DEFAULT_MANAGER_PASSWORD") ?? GenerateSecurePassword();
            var defaultUserPassword = Environment.GetEnvironmentVariable("AUTH_DEFAULT_USER_PASSWORD") ?? GenerateSecurePassword();

            // Create admin account if no admin exists
            if (!_users.Any(u => u.Role == UserRole.Admin))
            {
                _logger.LogWarning("No admin user found — creating default admin (username: admin)");
                _users.Add(new AppUser
                {
                    Id           = Guid.NewGuid(),
                    Username     = "admin",
                    PasswordHash = HashPassword(defaultAdminPassword),
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
                _logger.LogWarning("Creating default manager account (username: manager)");
                _users.Add(new AppUser
                {
                    Id           = Guid.NewGuid(),
                    Username     = "manager",
                    PasswordHash = HashPassword(defaultManagerPassword),
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
                _logger.LogWarning("Creating default user account (username: user)");
                _users.Add(new AppUser
                {
                    Id           = Guid.NewGuid(),
                    Username     = "user",
                    PasswordHash = HashPassword(defaultUserPassword),
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
    /// Generates a secure random password.
    /// </summary>
    private static string GenerateSecurePassword()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(12));
    }

    /// <summary>
    /// Loads users from database or JSON file into memory.
    /// Builds the username index for fast lookups.
    /// </summary>
    private void LoadUsers()
    {
        if (_useDatabase && _dbFactory != null)
        {
            LoadUsersFromDatabase();
        }
        else
        {
            LoadUsersFromFile();
        }
    }

    private void LoadUsersFromDatabase()
    {
        if (_dbFactory is null)
            throw new InvalidOperationException("DbFactory is not configured but LoadUsersFromDatabase was called.");
        
        try
        {
            using var db = _dbFactory.CreateDbContext();
            var entities = db.Users.AsNoTracking().ToList();
            
            _users.Clear();
            _usersByUsername.Clear();
            
            // Users table already maps to AppUser in Core
            foreach (var e in entities)
            {
                _users.Add(e);
                _usersByUsername[e.Username.ToLowerInvariant()] = e;
            }
            _logger.LogInformation("✅ Loaded {Count} users from database", _users.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load users from database, falling back to file");
            LoadUsersFromFile();
        }
    }

    private void LoadUsersFromFile()
    {
        try
        {
            if (string.IsNullOrEmpty(_usersFilePath) || !File.Exists(_usersFilePath)) return;
            var json  = File.ReadAllText(_usersFilePath);
            var users = JsonSerializer.Deserialize<List<AppUser>>(json);
            if (users != null)
            {
                _users.AddRange(users);
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
    /// Persists the in-memory user list to database or JSON file.
    /// </summary>
    private void SaveUsers()
    {
        if (_useDatabase && _dbFactory != null)
        {
            SaveUsersToDatabase();
        }
        else
        {
            SaveUsersToFile();
        }
    }

    private void SaveUsersToDatabase()
    {
        if (_dbFactory is null)
            throw new InvalidOperationException("DbFactory is not configured but SaveUsersToDatabase was called.");
        
        try
        {
            using var db = _dbFactory.CreateDbContext();
            
            foreach (var user in _users)
            {
                var existing = db.Users.Find(user.Id);
                if (existing == null)
                {
                    db.Users.Add(user);
                }
                else
                {
                    // Attach and update - let EF handle enum conversion
                    db.Entry(existing).CurrentValues.SetValues(user);
                }
            }
            db.SaveChanges();
            _logger.LogInformation("✅ Saved {Count} users to database", _users.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save users to database");
        }
    }

    private void SaveUsersToFile()
    {
        if (string.IsNullOrEmpty(_usersFilePath)) return;
        try
        {
            var dir = Path.GetDirectoryName(_usersFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_users,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_usersFilePath, json);
            _logger.LogInformation("✅ Saved {Count} users to {Path}", _users.Count, _usersFilePath);
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

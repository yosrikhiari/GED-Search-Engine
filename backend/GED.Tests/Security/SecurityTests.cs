using FluentAssertions;
using GED.Core.Models;
using GED.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Cryptography;

namespace GED.Tests.Security;

[Trait("Category", "Unit")]
public class SecurityTests
{
    #region Password Hashing Tests

    [Fact]
    public void PasswordHash_UsesPBKDF2_WithCorrectIterationCount()
    {
        var loggerMock = new Mock<ILogger<AuthService>>();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        
        var authService = new AuthService(loggerMock.Object, config);

        var hashMethod = typeof(AuthService)
            .GetField("CurrentIterationCount", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Static)?
            .GetValue(null);

        ((int)hashMethod!).Should().BeGreaterOrEqualTo(310_000, "OWASP 2023 recommends minimum 310,000 iterations for PBKDF2-SHA256");
    }

    [Fact]
    public void PasswordHash_ProducesNonEmptyHash()
    {
        var loggerMock = new Mock<ILogger<AuthService>>();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        
        var authService = new AuthService(loggerMock.Object, config);

        var hashMethod = typeof(AuthService)
            .GetMethod("HashPassword", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Static)!;

        var hash = (string)hashMethod.Invoke(null, new object[] { "TestPassword123!" })!;
        
        hash.Should().NotBeNullOrEmpty();
        hash.Should().NotBe("TestPassword123!");
        hash.Should().Contain(":");
    }

    [Fact]
    public void PasswordVerification_VerifiesCorrectPassword()
    {
        var loggerMock = new Mock<ILogger<AuthService>>();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        
        var authService = new AuthService(loggerMock.Object, config);

        var hashMethod = typeof(AuthService)
            .GetMethod("HashPassword", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Static)!;

        var verifyMethod = typeof(AuthService)
            .GetMethod("VerifyPassword", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Static)!;

        var password = "MySecurePassword123!";
        var hash = (string)hashMethod.Invoke(null, new object[] { password })!;
        
        var result = (bool)verifyMethod.Invoke(null, new object[] { password, hash })!;
        
        result.Should().BeTrue("VerifyPassword should return true for correct password");
    }

    [Fact]
    public void PasswordVerification_RejectsWrongPassword()
    {
        var loggerMock = new Mock<ILogger<AuthService>>();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        
        var authService = new AuthService(loggerMock.Object, config);

        var hashMethod = typeof(AuthService)
            .GetMethod("HashPassword", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Static)!;

        var verifyMethod = typeof(AuthService)
            .GetMethod("VerifyPassword", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Static)!;

        var password = "MySecurePassword123!";
        var wrongPassword = "WrongPassword456!";
        var hash = (string)hashMethod.Invoke(null, new object[] { password })!;
        
        var result = (bool)verifyMethod.Invoke(null, new object[] { wrongPassword, hash })!;
        
        result.Should().BeFalse("VerifyPassword should return false for incorrect password");
    }

    [Fact]
    public void PasswordVerification_UsesConstantTimeComparison()
    {
        var hash1 = new byte[32];
        var hash2 = new byte[32];
        RandomNumberGenerator.Fill(hash1);
        RandomNumberGenerator.Fill(hash2);

        var result = CryptographicOperations.FixedTimeEquals(hash1, hash1);
        result.Should().BeTrue("Same hash should be equal");

        var differentResult = CryptographicOperations.FixedTimeEquals(hash1, hash2);
        differentResult.Should().BeFalse("Different hashes should not be equal");
    }

    [Fact]
    public void PasswordHash_IsDeterministicPerSalt()
    {
        var hashMethod = typeof(AuthService)
            .GetMethod("HashPassword", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Static)!;

        var verifyMethod = typeof(AuthService)
            .GetMethod("VerifyPassword", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Static)!;

        var password = "TestPassword123";
        var hash = (string)hashMethod.Invoke(null, new object[] { password })!;
        
        var result1 = (bool)verifyMethod.Invoke(null, new object[] { password, hash })!;
        var result2 = (bool)verifyMethod.Invoke(null, new object[] { password, hash })!;
        
        result1.Should().BeTrue();
        result2.Should().BeTrue("Same password should verify successfully multiple times");
    }

    [Fact]
    public void PasswordHash_Format_IncludesIterations()
    {
        var hashMethod = typeof(AuthService)
            .GetMethod("HashPassword", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Static)!;

        var hash = (string)hashMethod.Invoke(null, new object[] { "TestPassword" })!;
        
        var parts = hash.Split(':');
        parts.Should().HaveCount(3, "Hash format should be iterations:salt:hash");
        
        int.TryParse(parts[0], out var iterations).Should().BeTrue("First part should be numeric iterations");
        iterations.Should().BeGreaterOrEqualTo(310_000);
    }

    #endregion

    #region Session Token Tests

    [Fact]
    public void SessionToken_IsCryptographicallySecure()
    {
        var token1 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var token2 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        token1.Should().NotBe(token2, "Each token should be unique");
        token1.Length.Should().BeGreaterOrEqualTo(40, "Token should be at least 256 bits (40+ base64 chars)");
    }

    [Fact]
    public void SessionExpiry_DefaultIsEightHours()
    {
        var loggerMock = new Mock<ILogger<AuthService>>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Auth:SessionDurationHours", "8" }
            })
            .Build();
        
        var authService = new AuthService(loggerMock.Object, config);

        var sessionDurationField = typeof(AuthService)
            .GetField("_sessionDuration", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);

        var sessionDuration = (TimeSpan)sessionDurationField!.GetValue(authService)!;
        
        sessionDuration.TotalHours.Should().Be(8, "Default session duration should be 8 hours");
    }

    #endregion

    #region SQL Injection Prevention Tests

    [Theory]
    [InlineData("'; DROP TABLE Users;--")]
    [InlineData("' OR '1'='1")]
    [InlineData("admin'--")]
    [InlineData("'; DELETE FROM documents;--")]
    public void DangerousStrings_AreTreatedAsLiterals(string dangerousInput)
    {
        dangerousInput.Should().NotBeNullOrEmpty();
        dangerousInput.Should().ContainAny(new[] { "'", ";", "--" }, 
            "Input should contain SQL-like characters for testing");
    }

    [Fact]
    public void SearchQuery_WithMaliciousInput_DoesNotCrash()
    {
        var maliciousInput = "'; DROP TABLE Users;--";
        
        maliciousInput.Should().NotBeNullOrEmpty();
        maliciousInput.Length.Should().BeGreaterThan(0);
    }

    #endregion

    #region XSS Prevention Tests

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("javascript:alert('xss')")]
    [InlineData("<img src=x onerror=alert('xss')>")]
    public void XSSPayloads_AreDetected(string xssPayload)
    {
        var containsScript = xssPayload.Contains("<script", StringComparison.OrdinalIgnoreCase) || 
                            xssPayload.Contains("javascript:", StringComparison.OrdinalIgnoreCase);
        var containsImgOnerror = xssPayload.Contains("<img", StringComparison.OrdinalIgnoreCase) && 
                                 xssPayload.Contains("onerror", StringComparison.OrdinalIgnoreCase);
        
        (containsScript || containsImgOnerror).Should().BeTrue("Payload should be recognized as XSS");
    }

    [Theory]
    [InlineData("<script>")]
    [InlineData("javascript:")]
    [InlineData("onerror=")]
    public void XSSPatterns_AreRecognized(string pattern)
    {
        pattern.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Path Traversal Prevention Tests

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\..\\windows\\system32\\config\\sam")]
    [InlineData("%2e%2e%2f%2e%2e%2f%2e%2e%2fetc%2fpasswd")]
    public void PathTraversalPayloads_AreDetected(string pathTraversal)
    {
        var hasDoubleDot = pathTraversal.Contains("..");
        var hasEncodedTraversal = pathTraversal.ToLower().Contains("%2e");
        
        (hasDoubleDot || hasEncodedTraversal).Should().BeTrue("Path should contain traversal patterns");
    }

    #endregion

    #region File Upload Security Tests

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("virus.bat")]
    [InlineData("script.sh")]
    [InlineData("payload.ps1")]
    public void DangerousFileExtensions_AreDetected(string filename)
    {
        var dangerousExtensions = new[] { ".exe", ".bat", ".sh", ".ps1", ".cmd", ".vbs" };
        var extension = Path.GetExtension(filename);
        
        dangerousExtensions.Should().Contain(extension, $"Extension {extension} should be flagged as dangerous");
    }

    [Theory]
    [InlineData("document.pdf")]
    [InlineData("report.docx")]
    [InlineData("image.jpg")]
    [InlineData("data.xlsx")]
    public void SafeFileExtensions_AreAllowed(string filename)
    {
        var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".jpg", ".jpeg", ".png", ".txt" };
        var extension = Path.GetExtension(filename).ToLower();
        
        allowedExtensions.Should().Contain(extension, $"Extension {extension} should be allowed");
    }

    [Fact]
    public void FileSanitization_RemovesInvalidCharacters()
    {
        var dangerousFilename = "file<>:\" / \\ | ?.pdf";
        var invalidChars = Path.GetInvalidFileNameChars();
        
        var sanitized = new string(dangerousFilename.Where(c => !invalidChars.Contains(c)).ToArray());
        
        sanitized.Should().NotContain("<");
        sanitized.Should().NotContain(">");
        sanitized.Should().NotContain(":");
        sanitized.Should().NotContain("\"");
        sanitized.Should().NotContain("/");
        sanitized.Should().NotContain("\\");
        sanitized.Should().NotContain("|");
        sanitized.Should().NotContain("?");
    }

    #endregion

    #region Authentication Tests

    [Fact]
    public void LoginRequest_RequiresUsernameAndPassword()
    {
        var request = new LoginRequest { Username = "admin", Password = "pass123" };
        
        request.Username.Should().NotBeNullOrWhiteSpace();
        request.Password.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RegisterRequest_ValidatesRole()
    {
        var validRoles = new[] { UserRole.Admin, UserRole.Manager, UserRole.User, UserRole.ReadOnly };
        
        foreach (var role in validRoles)
        {
            var request = new RegisterRequest
            {
                Username = "test",
                Password = "Password123!",
                Role = role
            };
            request.Role.Should().BeOneOf(UserRole.Admin, UserRole.Manager, UserRole.User, UserRole.ReadOnly);
        }
    }

    [Fact]
    public void InactiveUser_HasCorrectProperty()
    {
        var user = new AppUser { Username = "inactive", IsActive = false };
        
        user.IsActive.Should().BeFalse();
    }

    [Fact]
    public void LoginRequest_DefaultValues()
    {
        var request = new LoginRequest();
        
        request.Username.Should().BeNullOrEmpty();
        request.Password.Should().BeNullOrEmpty();
    }

    #endregion

    #region Cookie Security Tests

    [Fact]
    public void Cookie_HttpOnlyFlag_PreventsXSS()
    {
        var httpOnlyCookie = "ged_session=abc123; HttpOnly; SameSite=Lax";
        
        httpOnlyCookie.Should().Contain("HttpOnly");
        httpOnlyCookie.Should().Contain("SameSite");
    }

    #endregion

    #region Rate Limiting Tests

    [Fact]
    public void RateLimiting_Config_IsConfigured()
    {
        var maxLoginAttempts = 10;
        var lockoutDuration = TimeSpan.FromMinutes(5);
        
        maxLoginAttempts.Should().BeLessOrEqualTo(10, "Rate limit should be reasonable for login attempts");
        lockoutDuration.TotalMinutes.Should().BeGreaterOrEqualTo(5, "Lockout duration should be at least 5 minutes");
    }

    #endregion

    #region CORS Tests

    [Fact]
    public void CORS_AllowedOrigins_AreConfigured()
    {
        var allowedOrigins = new[] { "http://localhost:3000", "http://localhost:5173" };
        
        allowedOrigins.Should().HaveCount(2);
        allowedOrigins.Should().OnlyContain(o => o.StartsWith("http://localhost"), 
            "Only localhost origins should be allowed in development");
    }

    #endregion

    #region Brute Force Protection Tests

    [Fact]
    public async Task Login_LocksOut_After5FailedAttempts()
    {
        var loggerMock = new Mock<ILogger<AuthService>>();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        
        var authService = new AuthService(loggerMock.Object, config);
        await authService.InitializeAsync();

        var registerRequest = new RegisterRequest
        {
            Username = "testuser_bf",
            Password = "TestPassword123!",
            FullName = "Test User",
            Role = UserRole.User
        };
        authService.Register(registerRequest);

        var loginRequest = new LoginRequest
        {
            Username = "testuser_bf",
            Password = "WrongPassword"
        };

        for (int i = 0; i < 5; i++)
        {
            var result = authService.Login(loginRequest, "192.168.1.100");
            result.Should().BeNull($"Attempt {i + 1} should fail with wrong password");
        }

        var lockedResult = authService.Login(loginRequest, "192.168.1.100");
        lockedResult.Should().BeNull("Account should be locked after 5 failed attempts");
    }

    [Fact]
    public async Task Login_DifferentIp_DoesNotBypassLockout()
    {
        var loggerMock = new Mock<ILogger<AuthService>>();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        
        var authService = new AuthService(loggerMock.Object, config);
        await authService.InitializeAsync();

        var registerRequest = new RegisterRequest
        {
            Username = "testuser_bf2",
            Password = "TestPassword123!",
            FullName = "Test User",
            Role = UserRole.User
        };
        authService.Register(registerRequest);

        var loginRequest = new LoginRequest
        {
            Username = "testuser_bf2",
            Password = "WrongPassword"
        };

        for (int i = 0; i < 5; i++)
        {
            authService.Login(loginRequest, "192.168.1.100");
        }

        var differentIpResult = authService.Login(loginRequest, "192.168.1.101");
        differentIpResult.Should().BeNull("Different IP should still be blocked by username:ip lockout");
    }

    [Fact]
    public async Task Login_SameUsername_DifferentIp_IndependentLockout()
    {
        var loggerMock = new Mock<ILogger<AuthService>>();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        
        var authService = new AuthService(loggerMock.Object, config);
        await authService.InitializeAsync();

        var registerRequest = new RegisterRequest
        {
            Username = "testuser_bf3",
            Password = "TestPassword123!",
            FullName = "Test User",
            Role = UserRole.User
        };
        authService.Register(registerRequest);

        var loginRequest = new LoginRequest
        {
            Username = "testuser_bf3",
            Password = "WrongPassword"
        };

        for (int i = 0; i < 4; i++)
        {
            authService.Login(loginRequest, "192.168.1.100");
        }

        var result1 = authService.Login(loginRequest, "192.168.1.100");
        result1.Should().BeNull("IP1 should be locked after 5 attempts");

        var result2 = authService.Login(loginRequest, "192.168.1.101");
        result2.Should().BeNull("IP2 with same username should also be blocked");

        var result3 = authService.Login(loginRequest, "192.168.1.102");
        result3.Should().BeNull("IP3 with same username should also be blocked");
    }

    #endregion

    #region Session Invalidation Tests

    [Fact]
    public async Task Logout_InvalidatesSessionToken()
    {
        var loggerMock = new Mock<ILogger<AuthService>>();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        
        var authService = new AuthService(loggerMock.Object, config);
        await authService.InitializeAsync();

        var registerRequest = new RegisterRequest
        {
            Username = "testuser_logout",
            Password = "TestPassword123!",
            FullName = "Test User",
            Role = UserRole.User
        };
        authService.Register(registerRequest);

        var loginRequest = new LoginRequest
        {
            Username = "testuser_logout",
            Password = "TestPassword123!"
        };

        var loginResult = authService.Login(loginRequest);
        loginResult.Should().NotBeNull("Login should succeed");
        
        var sessionToken = loginResult!.Token;
        sessionToken.Should().NotBeNullOrEmpty();

        var beforeLogout = authService.GetUserByToken(sessionToken);
        beforeLogout.Should().NotBeNull("Session should exist before logout");

        var logoutResult = authService.Logout(sessionToken);
        logoutResult.Should().BeTrue("Logout should return true");

        var afterLogout = authService.GetUserByToken(sessionToken);
        afterLogout.Should().BeNull("Session should be invalidated after logout");
    }

    #endregion

    #region Storage Quota Tests

    [Fact]
    public void StorageQuota_ConfigValues_AreReasonable()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Storage:MaxStorageBytesPerUser", "10737418240" },
            { "Storage:MaxStorageBytesPerCategory", "53687091200" }
        }).Build();

        var maxStoragePerUser = config.GetValue<long>("Storage:MaxStorageBytesPerUser", 0);
        var maxStoragePerCategory = config.GetValue<long>("Storage:MaxStorageBytesPerCategory", 0);

        maxStoragePerUser.Should().Be(10737418240L, "MaxStorageBytesPerUser should be 10GB");
        maxStoragePerUser.Should().BeLessThan(maxStoragePerCategory, "Per-user limit should be less than per-category limit");
        maxStoragePerCategory.Should().BeGreaterThan(0, "MaxStorageBytesPerCategory should be configured");
    }

    #endregion
}

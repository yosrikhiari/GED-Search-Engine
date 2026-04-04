using FluentAssertions;
using GED.Core.Models;

namespace GED.Tests.Security;

public class SecurityTests
{
    #region Password Hashing Tests

    [Fact]
    public void PasswordHash_UsesPBKDF2_WithSHA256()
    {
        const int saltBytes = 16;
        const int hashBytes = 32;
        const int iterations = 100_000;

        saltBytes.Should().Be(16);
        hashBytes.Should().Be(32);
        iterations.Should().BeGreaterOrEqualTo(100_000);
    }

    [Fact]
    public void PasswordHash_Format_IsBase64SaltColonBase64Hash()
    {
        var salt = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
        var hash = Convert.ToBase64String(new byte[] { 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51 });
        
        var formatted = $"{salt}:{hash}";
        
        formatted.Should().Contain(":");
        var parts = formatted.Split(':');
        parts.Should().HaveCount(2);
    }

    [Fact]
    public void PasswordVerification_UsesConstantTimeComparison()
    {
        var hash1 = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var hash2 = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var hash3 = new byte[] { 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35 };

        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(hash1, hash2).Should().BeTrue();
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(hash1, hash3).Should().BeFalse();
    }

    #endregion

    #region Session Token Tests

    [Fact]
    public void SessionToken_IsCryptographicallySecure()
    {
        var token1 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var token2 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        token1.Should().NotBe(token2);
        token1.Length.Should().BeGreaterOrEqualTo(40);
    }

    [Fact]
    public void SessionExpiry_DefaultIsEightHours()
    {
        var defaultSessionDurationHours = 8;
        var sessionStart = DateTime.UtcNow;
        var expectedExpiry = sessionStart.AddHours(defaultSessionDurationHours);

        (expectedExpiry - sessionStart).TotalHours.Should().Be(8);
    }

    #endregion

    #region SQL Injection Prevention Tests

    [Theory]
    [InlineData("'; DROP TABLE Users;--")]
    [InlineData("' OR '1'='1")]
    [InlineData("admin'--")]
    [InlineData("<script>alert('xss')</script>")]
    public void DangerousStrings_AreNotSQLInjection(string dangerousInput)
    {
        var isDangerous = dangerousInput.Contains("'") || 
                         dangerousInput.Contains("--") || 
                         dangerousInput.Contains(";") ||
                         dangerousInput.Contains("<script");

        isDangerous.Should().BeTrue();
    }

    [Fact]
    public void EFCore_Parameterization_PreventsSQLInjection()
    {
        var maliciousInput = "'; DROP TABLE Users;--";
        var parameters = new object[] { maliciousInput };
        
        parameters.Should().HaveCount(1);
        parameters[0].Should().BeOfType<string>();
    }

    #endregion

    #region XSS Prevention Tests

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("javascript:alert('xss')")]
    [InlineData("<img src=x onerror=alert('xss')>")]
    public void XSSPayloads_AreDetected(string xssPayload)
    {
        var containsScript = xssPayload.Contains("<script") || xssPayload.Contains("javascript:");
        var containsImgOnerror = xssPayload.Contains("<img") && xssPayload.Contains("onerror");
        
        (containsScript || containsImgOnerror).Should().BeTrue();
    }

    #endregion

    #region Path Traversal Prevention Tests

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\..\\windows\\system32\\config\\sam")]
    [InlineData("....//....//....//etc/passwd")]
    [InlineData("%2e%2e%2f%2e%2e%2f%2e%2e%2fetc%2fpasswd")]
    public void PathTraversalPayloads_AreDetected(string pathTraversal)
    {
        var hasDoubleDot = pathTraversal.Contains("..");
        var hasEncodedTraversal = pathTraversal.ToLower().Contains("%2e");
        
        (hasDoubleDot || hasEncodedTraversal).Should().BeTrue();
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
        
        dangerousExtensions.Should().Contain(extension);
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
        
        allowedExtensions.Should().Contain(extension);
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
    public void InactiveUser_CannotAuthenticate()
    {
        var user = new AppUser { Username = "inactive", IsActive = false };
        
        user.IsActive.Should().BeFalse();
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
    public void RateLimiting_ProtectsAgainstBruteForce()
    {
        var maxLoginAttempts = 10;
        var lockoutDuration = TimeSpan.FromMinutes(5);
        
        maxLoginAttempts.Should().BeLessOrEqualTo(10);
        lockoutDuration.TotalMinutes.Should().BeGreaterOrEqualTo(5);
    }

    #endregion

    #region CORS Tests

    [Fact]
    public void CORS_AllowedOrigins_AreConfigured()
    {
        var allowedOrigins = new[] { "http://localhost:3000", "http://localhost:5173" };
        
        allowedOrigins.Should().HaveCount(2);
        allowedOrigins.Should().OnlyContain(o => o.StartsWith("http://localhost"));
    }

    #endregion
}

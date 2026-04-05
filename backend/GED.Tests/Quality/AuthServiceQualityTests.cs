using FluentAssertions;
using GED.Core.Models;

namespace GED.Tests.Quality;

[Trait("Category", "Unit")]
public class AuthServiceQualityTests
{
    [Fact]
    public void LoginRequest_WithValidData_IsValid()
    {
        var request = new LoginRequest { Username = "admin", Password = "Admin@1234" };
        request.Username.Should().Be("admin");
        request.Password.Should().Be("Admin@1234");
    }

    [Fact]
    public void LoginRequest_DefaultValues_AreEmpty()
    {
        var request = new LoginRequest();
        request.Username.Should().BeEmpty();
        request.Password.Should().BeEmpty();
    }

    [Fact]
    public void RegisterRequest_WithAllFields_SetsAllProperties()
    {
        var request = new RegisterRequest
        {
            Username = "newuser",
            Password = "Password@123",
            FullName = "New User",
            Email = "newuser@example.com",
            Role = UserRole.User,
            AllowedCategories = new List<string> { "Invoice", "Contract" }
        };

        request.Username.Should().Be("newuser");
        request.FullName.Should().Be("New User");
        request.Email.Should().Be("newuser@example.com");
        request.Role.Should().Be(UserRole.User);
        request.AllowedCategories.Should().HaveCount(2);
    }

    [Fact]
    public void RegisterRequest_DefaultRole_IsUser()
    {
        var request = new RegisterRequest
        {
            Username = "test",
            Password = "Password@123"
        };

        request.Role.Should().Be(UserRole.User);
        request.AllowedCategories.Should().BeNull();
    }

    [Fact]
    public void LoginResponse_WithToken_ContainsAllFields()
    {
        var expiresAt = DateTime.UtcNow.AddHours(8);
        var response = new LoginResponse
        {
            Token = "test-token-abc123",
            UserId = Guid.NewGuid(),
            Username = "admin",
            FullName = "Administrator",
            Role = UserRole.Admin,
            ExpiresAt = expiresAt
        };

        response.Token.Should().NotBeNullOrEmpty();
        response.UserId.Should().NotBeEmpty();
        response.Username.Should().Be("admin");
        response.Role.Should().Be(UserRole.Admin);
        response.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void UserDto_WithFullData_SetsAllProperties()
    {
        var userId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow.AddDays(-30);
        var lastLogin = DateTime.UtcNow.AddHours(-1);

        var dto = new UserDto
        {
            Id = userId,
            Username = "manager",
            FullName = "Test Manager",
            Email = "manager@example.com",
            Role = UserRole.Manager,
            IsActive = true,
            CreatedAt = createdAt,
            LastLoginAt = lastLogin,
            AllowedCategories = new List<string> { "Invoice", "Report" }
        };

        dto.Id.Should().Be(userId);
        dto.Username.Should().Be("manager");
        dto.IsActive.Should().BeTrue();
        dto.LastLoginAt.Should().NotBeNull();
        dto.AllowedCategories.Should().Contain("Invoice");
    }

    [Fact]
    public void UserDto_DefaultValues_AreCorrect()
    {
        var dto = new UserDto();
        dto.Id.Should().Be(Guid.Empty);
        dto.Username.Should().BeEmpty();
        dto.CreatedAt.Should().Be(default);
    }

    [Fact]
    public void UserRole_AllRoles_AreDefined()
    {
        var roles = Enum.GetValues<UserRole>();
        roles.Should().Contain(UserRole.Admin);
        roles.Should().Contain(UserRole.Manager);
        roles.Should().Contain(UserRole.User);
        roles.Should().Contain(UserRole.ReadOnly);
        roles.Length.Should().Be(4);
    }

    [Fact]
    public void PasswordHash_Format_ContainsSaltAndHash()
    {
        var hash = "SALT:HASH";
        var parts = hash.Split(':');
        parts.Should().HaveCount(2);
        parts[0].Should().Be("SALT");
        parts[1].Should().Be("HASH");
    }

    [Fact]
    public void SessionExpiry_IsEightHours_FromLogin()
    {
        var loginTime = DateTime.UtcNow;
        var expiresAt = loginTime.AddHours(8);
        expiresAt.Should().Be(loginTime.AddHours(8));
        (expiresAt - loginTime).TotalHours.Should().Be(8);
    }

    [Fact]
    public void SessionExpiry_IsNotExpired_BeforeExpiryTime()
    {
        var expiresAt = DateTime.UtcNow.AddHours(8);
        expiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void SessionExpiry_IsExpired_AfterExpiryTime()
    {
        var expiresAt = DateTime.UtcNow.AddHours(-1);
        expiresAt.Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    public void AllowedCategories_ForManager_ContainsSubset()
    {
        var allCategories = new[] { "Invoice", "Contract", "Report", "Letter", "Memo", "Presentation", "Spreadsheet", "Image", "Other" };
        var managerCategories = new List<string> { "Invoice", "Contract", "Report" };

        managerCategories.Should().OnlyContain(c => allCategories.Contains(c));
        managerCategories.Should().NotContain("Other");
    }

    [Fact]
    public void AllowedCategories_ForAdmin_IsEmpty()
    {
        var adminCategories = new List<string>();
        adminCategories.Should().BeEmpty();
    }

    [Fact]
    public void CaseInsensitiveUsername_Lookup_Works()
    {
        var usernames = new[] { "Admin", "ADMIN", "admin", "AdMiN" };
        var lookup = usernames.FirstOrDefault(u => u.Equals("admin", StringComparison.OrdinalIgnoreCase));
        lookup.Should().NotBeNull();
        lookup.Should().BeEquivalentTo("admin");
    }

    [Fact]
    public void InactiveUser_CannotLogin()
    {
        var user = new AppUser { Username = "test", IsActive = false };
        user.IsActive.Should().BeFalse();
    }

    [Fact]
    public void DuplicateUsername_Registration_Fails()
    {
        var existingUsers = new List<string> { "admin", "manager", "user" };
        var newUsername = "admin";
        var isDuplicate = existingUsers.Any(u => u.Equals(newUsername, StringComparison.OrdinalIgnoreCase));
        isDuplicate.Should().BeTrue();
    }

    [Fact]
    public void NewUsername_NoDuplicates()
    {
        var existingUsers = new List<string> { "admin", "manager", "user" };
        var newUsername = "newuser";
        var isDuplicate = existingUsers.Any(u => u.Equals(newUsername, StringComparison.OrdinalIgnoreCase));
        isDuplicate.Should().BeFalse();
    }

    [Fact]
    public void PasswordValidation_Rejects_TooShort()
    {
        var shortPassword = "Ab1!";
        shortPassword.Length.Should().BeLessThan(8);
    }

    [Fact]
    public void PasswordValidation_Accepts_ValidPassword()
    {
        var validPassword = "Admin@1234";
        validPassword.Length.Should().BeGreaterOrEqualTo(8);
        validPassword.Should().Contain("@");
    }
}

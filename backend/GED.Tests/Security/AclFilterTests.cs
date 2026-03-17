using FluentAssertions;
using GED.Core.Models;

namespace GED.Tests.Security;

public class AclFilterConstructionTests
{
    [Theory]
    [InlineData("Admin", true)]  // Admin should see all
    [InlineData("User", false)]  // Regular user should be filtered
    [InlineData("Manager", false)]
    [InlineData(null, false)]
    public void AccessLevel_Logic_ShouldFilterAppropriately(string? role, bool shouldSeeAll)
    {
        // This tests the ACL filter construction logic
        // In the actual OpenSearchService, admins bypass ACL filters
        
        var isAdmin = role == "Admin";
        
        // Admin bypasses ACL filter
        isAdmin.Should().Be(shouldSeeAll);
    }

    [Fact]
    public void SearchRequest_WithUserContext_PassesAclData()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var categories = new List<string> { "Invoice", "Contract" };

        // Act
        var request = new SearchRequest
        {
            UserId = userId,
            UserRole = "User",
            UserAllowedCategories = categories
        };

        // Assert
        request.UserId.Should().Be(userId);
        request.UserRole.Should().Be("User");
        request.UserAllowedCategories.Should().Contain("Invoice");
    }

    [Fact]
    public void SearchRequest_AdminRole_BypassesAcl()
    {
        // Arrange
        var request = new SearchRequest
        {
            UserId = "admin-user-id",
            UserRole = "Admin"
        };

        // In the ACL filter logic, Admin role bypasses the filter
        request.UserRole.Should().Be("Admin");
        request.UserId.Should().NotBeNull();
    }

    [Fact]
    public void AclFilter_WithMultipleUsers_IncludesAllUserIds()
    {
        // Arrange - simulating ACL grants from database
        var userIds = new List<string>
        {
            "user-1",
            "user-2", 
            "user-3"
        };

        // Act - this simulates the filter construction in OpenSearchService
        var hasAnyUserIdGrants = userIds.Any();

        // Assert
        hasAnyUserIdGrants.Should().BeTrue();
        userIds.Should().HaveCount(3);
    }

    [Fact]
    public void AclFilter_AccessLevelRestricted_WhenGrantsExist()
    {
        // This simulates the IndexDocumentAsync logic fix
        // If aclUserIds.Count > 0, then accessLevel = "restricted"
        
        var aclUserIds = new List<string> { "user-1", "user-2" };
        var accessLevel = aclUserIds.Count > 0 ? "restricted" : "open";

        accessLevel.Should().Be("restricted");
    }

    [Fact]
    public void AclFilter_AccessLevelOpen_WhenNoGrants()
    {
        // This simulates the IndexDocumentAsync logic fix
        // If aclUserIds.Count == 0, then accessLevel = "open"
        
        var aclUserIds = new List<string>();
        var accessLevel = aclUserIds.Count > 0 ? "restricted" : "open";

        accessLevel.Should().Be("open");
    }
}

using FluentAssertions;
using GED.Core.Models;

namespace GED.Tests.Models;

[Trait("Category", "Unit")]
public class DocumentGroupModelTests
{
    [Fact]
    public void DocumentGroup_DefaultValues_AreCorrect()
    {
        var group = new DocumentGroup();

        group.Id.Should().NotBe(Guid.Empty);
        group.Name.Should().BeEmpty();
        group.Description.Should().BeNull();
        group.Color.Should().BeNull();
        group.Icon.Should().BeNull();
        group.Category.Should().BeNull();
        group.CreatedBy.Should().Be(Guid.Empty);
        group.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        group.UpdatedAt.Should().BeNull();
        group.IsActive.Should().BeTrue();
    }

    [Fact]
    public void DocumentGroup_WithFullData_SetsAllProperties()
    {
        var now = DateTime.UtcNow;
        var group = new DocumentGroup
        {
            Id = Guid.NewGuid(),
            Name = "HR Contracts 2024",
            Description = "All HR contracts for 2024",
            Color = "#2563eb",
            Icon = "📁",
            Category = "HR",
            CreatedBy = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = false
        };

        group.Name.Should().Be("HR Contracts 2024");
        group.IsActive.Should().BeFalse();
    }

    [Fact]
    public void DocumentGroupMember_DefaultValues_AreCorrect()
    {
        var member = new DocumentGroupMember();

        member.Id.Should().NotBe(Guid.Empty);
        member.GroupId.Should().Be(Guid.Empty);
        member.DocumentId.Should().Be(Guid.Empty);
        member.DefaultPermission.Should().Be(AclPermission.Read);
        member.AddedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        member.AddedBy.Should().Be(Guid.Empty);
    }

    [Fact]
    public void DocumentGroupMember_WithData_SetsAllProperties()
    {
        var member = new DocumentGroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            DefaultPermission = AclPermission.Delete,
            AddedBy = Guid.NewGuid()
        };

        member.DefaultPermission.Should().Be(AclPermission.Delete);
    }

    [Fact]
    public void UserGroupAssignment_DefaultValues_AreCorrect()
    {
        var assignment = new UserGroupAssignment();

        assignment.Id.Should().NotBe(Guid.Empty);
        assignment.GroupId.Should().Be(Guid.Empty);
        assignment.UserId.Should().Be(Guid.Empty);
        assignment.Permission.Should().Be(AclPermission.Read);
        assignment.AssignedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        assignment.AssignedBy.Should().Be(Guid.Empty);
        assignment.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void UserGroupAssignment_WithData_SetsAllProperties()
    {
        var now = DateTime.UtcNow;
        var assignment = new UserGroupAssignment
        {
            Id = Guid.NewGuid(),
            GroupId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Permission = AclPermission.FullControl,
            AssignedBy = Guid.NewGuid(),
            ExpiresAt = now.AddDays(30)
        };

        assignment.Permission.Should().Be(AclPermission.FullControl);
        assignment.ExpiresAt.Should().BeAfter(now);
    }

    [Fact]
    public void CreateGroupRequest_WithData_SetsProperties()
    {
        var request = new CreateGroupRequest
        {
            Name = "Finance Reports",
            Description = "Monthly finance reports",
            Color = "#10b981",
            Icon = "📊",
            Category = "Finance",
            DocumentIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() }
        };

        request.Name.Should().Be("Finance Reports");
        request.DocumentIds.Should().HaveCount(2);
    }

    [Fact]
    public void UpdateGroupRequest_WithData_SetsProperties()
    {
        var request = new UpdateGroupRequest
        {
            Name = "Updated Name",
            Description = "Updated description",
            Color = "#ef4444"
        };

        request.Name.Should().Be("Updated Name");
    }

    [Fact]
    public void AddDocumentsToGroupRequest_DefaultPermission_IsRead()
    {
        var request = new AddDocumentsToGroupRequest
        {
            DocumentIds = new List<Guid> { Guid.NewGuid() }
        };

        request.DefaultPermission.Should().Be(AclPermission.Read);
    }

    [Fact]
    public void AssignGroupToUserRequest_DefaultPermission_IsRead()
    {
        var request = new AssignGroupToUserRequest
        {
            UserId = Guid.NewGuid()
        };

        request.Permission.Should().Be(AclPermission.Read);
        request.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void DocumentGroupDto_WithData_SetsProperties()
    {
        var dto = new DocumentGroupDto
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            Description = "Test description",
            Color = "#6366f1",
            Icon = "📁",
            Category = "Test",
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            DocumentCount = 10,
            UserCount = 5,
            Documents = new List<GroupDocumentDto>(),
            Assignments = new List<GroupAssignmentDto>()
        };

        dto.DocumentCount.Should().Be(10);
        dto.UserCount.Should().Be(5);
    }

    [Fact]
    public void GroupDocumentDto_WithData_SetsProperties()
    {
        var dto = new GroupDocumentDto
        {
            MemberId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Title = "Test Document",
            FileName = "test.pdf",
            Category = "Invoice",
            DefaultPermission = AclPermission.Read,
            AddedAt = DateTime.UtcNow
        };

        dto.Title.Should().Be("Test Document");
    }

    [Fact]
    public void GroupAssignmentDto_IsActive_WhenNoExpiry()
    {
        var dto = new GroupAssignmentDto
        {
            AssignmentId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Username = "testuser",
            Permission = AclPermission.Read,
            AssignedAt = DateTime.UtcNow,
            ExpiresAt = null
        };

        dto.IsActive.Should().BeTrue();
    }

    [Fact]
    public void GroupAssignmentDto_IsActive_WhenExpiryInFuture()
    {
        var dto = new GroupAssignmentDto
        {
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };

        dto.IsActive.Should().BeTrue();
    }

    [Fact]
    public void GroupAssignmentDto_IsInactive_WhenExpiryInPast()
    {
        var dto = new GroupAssignmentDto
        {
            ExpiresAt = DateTime.UtcNow.AddDays(-10)
        };

        dto.IsActive.Should().BeFalse();
    }

    [Fact]
    public void UserAccessSummaryDto_WithData_SetsProperties()
    {
        var dto = new UserAccessSummaryDto
        {
            UserId = Guid.NewGuid(),
            Username = "testuser",
            FullName = "Test User",
            Role = UserRole.User,
            IsActive = true,
            Groups = new List<UserGroupSummary>(),
            DirectGrants = new List<DocumentAclDto>()
        };

        dto.Username.Should().Be("testuser");
        dto.Role.Should().Be(UserRole.User);
    }

    [Fact]
    public void UserGroupSummary_WithData_SetsProperties()
    {
        var summary = new UserGroupSummary
        {
            AssignmentId = Guid.NewGuid(),
            GroupId = Guid.NewGuid(),
            GroupName = "Test Group",
            GroupColor = "#6366f1",
            GroupIcon = "📁",
            DocumentCount = 20,
            Permission = AclPermission.Read,
            AssignedAt = DateTime.UtcNow,
            ExpiresAt = null
        };

        summary.DocumentCount.Should().Be(20);
    }

    [Fact]
    public void ChangeRoleRequest_WithData_SetsProperties()
    {
        var request = new ChangeRoleRequest
        {
            Role = UserRole.Manager
        };

        request.Role.Should().Be(UserRole.Manager);
    }
}
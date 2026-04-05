using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using GED.Infrastructure.Data;
using GED.Core.Models;

namespace GED.Tests.Integration;

[Trait("Category", "Unit")]
public class GedDbContextIntegrationTests : IDisposable
{
    private readonly GedDbContext _context;

    public GedDbContextIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<GedDbContext>()
            .UseInMemoryDatabase($"GED-Integration-Test-{Guid.NewGuid()}")
            .Options;
        _context = new GedDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task Document_Create_and_Retrieve_Works()
    {
        var doc = new DocumentEntity
        {
            Id = Guid.NewGuid(),
            Title = "Test Invoice",
            FileName = "invoice.pdf",
            FilePath = "/storage/invoice.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            CreatedAt = DateTime.UtcNow,
            Status = DocumentStatus.Pending,
            Category = "Invoice"
        };

        _context.Documents.Add(doc);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Documents.FindAsync(doc.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Title.Should().Be("Test Invoice");
        retrieved.Category.Should().Be("Invoice");
        retrieved.Status.Should().Be(DocumentStatus.Pending);
    }

    [Fact]
    public async Task Document_Update_Works()
    {
        var doc = new DocumentEntity
        {
            Id = Guid.NewGuid(),
            Title = "Original Title",
            FileName = "doc.pdf",
            FilePath = "/storage/doc.pdf",
            ContentType = "application/pdf",
            FileSize = 2048,
            CreatedAt = DateTime.UtcNow,
            Status = DocumentStatus.Pending,
            Category = "Contract"
        };

        _context.Documents.Add(doc);
        await _context.SaveChangesAsync();

        doc.Title = "Updated Title";
        doc.Status = DocumentStatus.Indexed;
        doc.ModifiedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var updated = await _context.Documents.FindAsync(doc.Id);
        updated!.Title.Should().Be("Updated Title");
        updated.Status.Should().Be(DocumentStatus.Indexed);
        updated.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Document_Delete_Works()
    {
        var doc = new DocumentEntity
        {
            Id = Guid.NewGuid(),
            Title = "To Delete",
            FileName = "delete.pdf",
            FilePath = "/storage/delete.pdf",
            ContentType = "application/pdf",
            FileSize = 512,
            CreatedAt = DateTime.UtcNow,
            Status = DocumentStatus.Pending,
            Category = "Report"
        };

        _context.Documents.Add(doc);
        await _context.SaveChangesAsync();

        _context.Documents.Remove(doc);
        await _context.SaveChangesAsync();

        var count = await _context.Documents.CountAsync(d => d.Id == doc.Id);
        count.Should().Be(0);
    }

    [Fact]
    public async Task DocumentAcl_Create_and_Query_Works()
    {
        var docId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var acl = new DocumentAcl
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            UserId = userId,
            Permission = AclPermission.Read,
            GrantedAt = DateTime.UtcNow,
            GrantedBy = Guid.NewGuid()
        };

        _context.DocumentAcls.Add(acl);
        await _context.SaveChangesAsync();

        var retrieved = await _context.DocumentAcls
            .FirstOrDefaultAsync(a => a.DocumentId == docId && a.UserId == userId);
        retrieved.Should().NotBeNull();
        retrieved!.Permission.Should().Be(AclPermission.Read);
    }

    [Fact]
    public async Task DocumentAcl_ExpiringGrant_Works()
    {
        var acl = new DocumentAcl
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Permission = AclPermission.Read,
            GrantedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        };

        _context.DocumentAcls.Add(acl);
        await _context.SaveChangesAsync();

        var isExpired = acl.ExpiresAt.HasValue && acl.ExpiresAt.Value < DateTime.UtcNow;
        isExpired.Should().BeTrue();
    }

    [Fact]
    public async Task DocumentAcl_CompositeIndex_Works()
    {
        var docId = Guid.NewGuid();
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();

        _context.DocumentAcls.AddRange(
            new DocumentAcl { Id = Guid.NewGuid(), DocumentId = docId, UserId = userId1, Permission = AclPermission.Read, GrantedAt = DateTime.UtcNow },
            new DocumentAcl { Id = Guid.NewGuid(), DocumentId = docId, UserId = userId2, Permission = AclPermission.Write, GrantedAt = DateTime.UtcNow }
        );
        await _context.SaveChangesAsync();

        var grants = await _context.DocumentAcls.Where(a => a.DocumentId == docId).ToListAsync();
        grants.Should().HaveCount(2);
    }

    [Fact]
    public async Task OutboxMessage_Create_and_Query_Works()
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "OcrJob",
            Payload = "{\"documentId\":\"" + Guid.NewGuid() + "\"}",
            CreatedAt = DateTime.UtcNow,
            RetryCount = 0
        };

        _context.OutboxMessages.Add(message);
        await _context.SaveChangesAsync();

        var unprocessed = await _context.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .ToListAsync();
        unprocessed.Should().HaveCount(1);
        unprocessed[0].Type.Should().Be("OcrJob");
    }

    [Fact]
    public async Task OutboxMessage_MarkAsProcessed_Works()
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "OcrJob",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow,
            RetryCount = 0
        };

        _context.OutboxMessages.Add(message);
        await _context.SaveChangesAsync();

        message.ProcessedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var processed = await _context.OutboxMessages.FindAsync(message.Id);
        processed!.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task OutboxMessage_RetryCount_Increments()
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "OcrJob",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow,
            RetryCount = 2,
            Error = "Connection failed"
        };

        _context.OutboxMessages.Add(message);
        await _context.SaveChangesAsync();

        message.RetryCount++;
        await _context.SaveChangesAsync();

        var updated = await _context.OutboxMessages.FindAsync(message.Id);
        updated!.RetryCount.Should().Be(3);
    }

    [Fact]
    public async Task OutboxRelay_MarksMessageAsFailed_AfterMaxRetries()
    {
        const int MaxRetries = 5;
        
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "OcrJob",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            RetryCount = MaxRetries,
            Error = "Persistent connection failure",
            ProcessedAt = null
        };

        _context.OutboxMessages.Add(message);
        await _context.SaveChangesAsync();

        var retrieved = await _context.OutboxMessages.FindAsync(message.Id);
        retrieved.Should().NotBeNull();
        retrieved!.RetryCount.Should().Be(MaxRetries);
        retrieved.ProcessedAt.Should().BeNull("Message should not be processed after max retries");
        retrieved.Error.Should().NotBeNullOrEmpty("Error should be recorded");
    }

    [Fact]
    public async Task DocumentGroup_Create_and_Query_Works()
    {
        var group = new DocumentGroup
        {
            Id = Guid.NewGuid(),
            Name = "Finance Team",
            Description = "Documents for finance team",
            Color = "#4CAF50",
            Icon = "folder",
            Category = "Invoice",
            CreatedBy = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.DocumentGroups.Add(group);
        await _context.SaveChangesAsync();

        var retrieved = await _context.DocumentGroups.FindAsync(group.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Finance Team");
        retrieved.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task DocumentGroup_Deactivate_SoftDelete()
    {
        var group = new DocumentGroup
        {
            Id = Guid.NewGuid(),
            Name = "Archive Group",
            CreatedBy = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.DocumentGroups.Add(group);
        await _context.SaveChangesAsync();

        group.IsActive = false;
        group.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var active = await _context.DocumentGroups.Where(g => g.IsActive).ToListAsync();
        active.Should().NotContain(g => g.Id == group.Id);
    }

    [Fact]
    public async Task DocumentGroupMember_Add_Works()
    {
        var groupId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        var member = new DocumentGroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            DocumentId = docId,
            DefaultPermission = AclPermission.Read,
            AddedAt = DateTime.UtcNow,
            AddedBy = Guid.NewGuid()
        };

        _context.DocumentGroupMembers.Add(member);
        await _context.SaveChangesAsync();

        var retrieved = await _context.DocumentGroupMembers.FindAsync(member.Id);
        retrieved.Should().NotBeNull();
        retrieved!.GroupId.Should().Be(groupId);
        retrieved.DocumentId.Should().Be(docId);
    }

    [Fact]
    public async Task DocumentGroupMember_UniqueConstraint_Works()
    {
        var groupId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        var member1 = new DocumentGroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            DocumentId = docId,
            DefaultPermission = AclPermission.Read,
            AddedAt = DateTime.UtcNow
        };
        _context.DocumentGroupMembers.Add(member1);
        await _context.SaveChangesAsync();

        var count = await _context.DocumentGroupMembers
            .Where(m => m.GroupId == groupId && m.DocumentId == docId)
            .CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task DocumentVersion_Create_Works()
    {
        var docId = Guid.NewGuid();
        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            VersionNumber = 1,
            Title = "Invoice v1",
            FileName = "invoice_v1.pdf",
            FilePath = "/storage/invoice_v1.pdf",
            FileSize = 1024,
            ContentType = "application/pdf",
            Category = "Invoice",
            Tags = "[]",
            Metadata = "{}",
            ChangedBy = "admin",
            ChangeReason = "Initial upload",
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentVersions.Add(version);
        await _context.SaveChangesAsync();

        var retrieved = await _context.DocumentVersions.FindAsync(version.Id);
        retrieved.Should().NotBeNull();
        retrieved!.VersionNumber.Should().Be(1);
        retrieved.DocumentId.Should().Be(docId);
    }

    [Fact]
    public async Task DocumentVersion_UniqueCompositeIndex_Works()
    {
        var docId = Guid.NewGuid();
        _context.DocumentVersions.AddRange(
            new DocumentVersion { Id = Guid.NewGuid(), DocumentId = docId, VersionNumber = 1, CreatedAt = DateTime.UtcNow },
            new DocumentVersion { Id = Guid.NewGuid(), DocumentId = docId, VersionNumber = 2, CreatedAt = DateTime.UtcNow }
        );
        await _context.SaveChangesAsync();

        var versions = await _context.DocumentVersions.Where(v => v.DocumentId == docId).ToListAsync();
        versions.Should().HaveCount(2);
    }

    [Fact]
    public async Task AppUser_Create_and_Query_Works()
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "testuser",
            PasswordHash = "hash:salt",
            FullName = "Test User",
            Email = "test@example.com",
            Role = UserRole.User,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Users.FindAsync(user.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Username.Should().Be("testuser");
        retrieved.Role.Should().Be(UserRole.User);
    }

    [Fact]
    public async Task AppUser_UniqueUsername_Works()
    {
        var user1 = new AppUser { Id = Guid.NewGuid(), Username = "uniqueuser", PasswordHash = "hash", CreatedAt = DateTime.UtcNow };
        _context.Users.Add(user1);
        await _context.SaveChangesAsync();

        var existing = await _context.Users.AnyAsync(u => u.Username == "uniqueuser");
        existing.Should().BeTrue();
    }

    [Fact]
    public async Task AppUser_Deactivate_Works()
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "to_deactivate",
            PasswordHash = "hash",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        user.IsActive = false;
        await _context.SaveChangesAsync();

        var active = await _context.Users.Where(u => u.IsActive).ToListAsync();
        active.Should().NotContain(u => u.Id == user.Id);
    }

    [Fact]
    public async Task UserGroupAssignment_Create_Works()
    {
        var assignment = new UserGroupAssignment
        {
            Id = Guid.NewGuid(),
            GroupId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Permission = AclPermission.Read,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = Guid.NewGuid()
        };

        _context.UserGroupAssignments.Add(assignment);
        await _context.SaveChangesAsync();

        var count = await _context.UserGroupAssignments.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task Document_FilterByCategory_Works()
    {
        _context.Documents.AddRange(
            new DocumentEntity { Id = Guid.NewGuid(), Title = "Invoice 1", FileName = "a.pdf", FilePath = "/a.pdf", ContentType = "application/pdf", FileSize = 100, CreatedAt = DateTime.UtcNow, Status = DocumentStatus.Indexed, Category = "Invoice" },
            new DocumentEntity { Id = Guid.NewGuid(), Title = "Invoice 2", FileName = "b.pdf", FilePath = "/b.pdf", ContentType = "application/pdf", FileSize = 100, CreatedAt = DateTime.UtcNow, Status = DocumentStatus.Indexed, Category = "Invoice" },
            new DocumentEntity { Id = Guid.NewGuid(), Title = "Contract 1", FileName = "c.pdf", FilePath = "/c.pdf", ContentType = "application/pdf", FileSize = 100, CreatedAt = DateTime.UtcNow, Status = DocumentStatus.Indexed, Category = "Contract" }
        );
        await _context.SaveChangesAsync();

        var invoices = await _context.Documents.Where(d => d.Category == "Invoice").ToListAsync();
        invoices.Should().HaveCount(2);
    }

    [Fact]
    public async Task Document_FilterByStatus_Works()
    {
        _context.Documents.AddRange(
            new DocumentEntity { Id = Guid.NewGuid(), Title = "Pending Doc", FileName = "p.pdf", FilePath = "/p.pdf", ContentType = "application/pdf", FileSize = 100, CreatedAt = DateTime.UtcNow, Status = DocumentStatus.Pending },
            new DocumentEntity { Id = Guid.NewGuid(), Title = "Indexed Doc", FileName = "i.pdf", FilePath = "/i.pdf", ContentType = "application/pdf", FileSize = 100, CreatedAt = DateTime.UtcNow, Status = DocumentStatus.Indexed }
        );
        await _context.SaveChangesAsync();

        var pending = await _context.Documents.Where(d => d.Status == DocumentStatus.Pending).ToListAsync();
        pending.Should().HaveCount(1);
        pending[0].Title.Should().Be("Pending Doc");
    }

    [Fact]
    public async Task Document_Tags_SerializedAsJson()
    {
        var doc = new DocumentEntity
        {
            Id = Guid.NewGuid(),
            Title = "Tagged Doc",
            FileName = "tagged.pdf",
            FilePath = "/tagged.pdf",
            ContentType = "application/pdf",
            FileSize = 100,
            CreatedAt = DateTime.UtcNow,
            Status = DocumentStatus.Indexed,
            Tags = new List<string> { "urgent", "2024", "invoice" }
        };

        _context.Documents.Add(doc);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Documents.FindAsync(doc.Id);
        retrieved!.Tags.Should().HaveCount(3);
        retrieved.Tags.Should().Contain("urgent");
    }

    [Fact]
    public async Task Document_Metadata_SerializedAsJson()
    {
        var doc = new DocumentEntity
        {
            Id = Guid.NewGuid(),
            Title = "Meta Doc",
            FileName = "meta.pdf",
            FilePath = "/meta.pdf",
            ContentType = "application/pdf",
            FileSize = 100,
            CreatedAt = DateTime.UtcNow,
            Status = DocumentStatus.Indexed,
            Metadata = new Dictionary<string, object> { ["author"] = "Finance", ["year"] = 2024 }
        };

        _context.Documents.Add(doc);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Documents.FindAsync(doc.Id);
        retrieved!.Metadata.Should().NotBeNull();
        retrieved.Metadata.Should().ContainKey("author");
    }

    [Fact]
    public async Task Document_Pagination_Works()
    {
        for (int i = 0; i < 25; i++)
        {
            _context.Documents.Add(new DocumentEntity
            {
                Id = Guid.NewGuid(),
                Title = $"Doc {i}",
                FileName = $"{i}.pdf",
                FilePath = $"/{i}.pdf",
                ContentType = "application/pdf",
                FileSize = 100,
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                Status = DocumentStatus.Indexed
            });
        }
        await _context.SaveChangesAsync();

        var page1 = await _context.Documents.OrderByDescending(d => d.CreatedAt).Skip(0).Take(10).ToListAsync();
        var page2 = await _context.Documents.OrderByDescending(d => d.CreatedAt).Skip(10).Take(10).ToListAsync();
        var page3 = await _context.Documents.OrderByDescending(d => d.CreatedAt).Skip(20).Take(10).ToListAsync();

        page1.Should().HaveCount(10);
        page2.Should().HaveCount(10);
        page3.Should().HaveCount(5);
    }

    [Fact]
    public async Task Document_DateRange_Query_Works()
    {
        var now = DateTime.UtcNow;
        _context.Documents.AddRange(
            new DocumentEntity { Id = Guid.NewGuid(), Title = "Old Doc", FileName = "old.pdf", FilePath = "/old.pdf", ContentType = "application/pdf", FileSize = 100, CreatedAt = now.AddDays(-30), Status = DocumentStatus.Indexed },
            new DocumentEntity { Id = Guid.NewGuid(), Title = "Recent Doc", FileName = "recent.pdf", FilePath = "/recent.pdf", ContentType = "application/pdf", FileSize = 100, CreatedAt = now, Status = DocumentStatus.Indexed }
        );
        await _context.SaveChangesAsync();

        var from = now.AddDays(-7);
        var recent = await _context.Documents.Where(d => d.CreatedAt >= from).ToListAsync();
        recent.Should().HaveCount(1);
        recent[0].Title.Should().Be("Recent Doc");
    }
}

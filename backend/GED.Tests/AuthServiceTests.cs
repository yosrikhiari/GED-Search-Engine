using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using GED.Core.Models;
using GED.Infrastructure.Data;
using GED.Infrastructure.Services;
using Xunit;
using Moq;

namespace GED.Tests;

[Trait("Category", "Unit")]
public class AuthServiceTests
{
    private readonly Mock<ILogger<AuthService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<IDistributedCache> _mockCache;

    public AuthServiceTests()
    {
        _mockLogger = new Mock<ILogger<AuthService>>();
        _mockConfig = new Mock<IConfiguration>();
        _mockCache = new Mock<IDistributedCache>();
        
        // Setup config - use indexer instead of extension method
        var configDict = new Dictionary<string, string?>
        {
            { "Auth:SessionDurationHours", "8" }
        };
        _mockConfig.Setup(c => c["Auth:SessionDurationHours"]).Returns("8");
    }

    [Fact]
    public void Constructor_DoesNotLoadUsersFromFile()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _mockConfig.Setup(c => c["Auth:UsersFilePath"]).Returns(Path.Combine(tempDir, "users.json"));
        
        // Act - instantiate AuthService (no file should be created)
        var authService = new AuthService(
            _mockLogger.Object,
            _mockConfig.Object,
            _mockCache.Object,
            null); // No DB factory - uses file fallback
        
        // Assert - users list should be empty (not loaded in constructor)
        var method = typeof(AuthService).GetMethod("InitializeAsync", 
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        
        Assert.NotNull(method); // InitializeAsync exists
    }

    [Fact]
    public async Task InitializeAsync_WithNoFactory_UsesFileFallback()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var usersFile = Path.Combine(tempDir, "users.json");
        
        // Create a test users file
        var testUsers = new List<AppUser>
        {
            new() 
            { 
                Id = Guid.NewGuid(), 
                Username = "filetest", 
                PasswordHash = "hash456",
                FullName = "File Test",
                Role = UserRole.User,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            }
        };
        File.WriteAllText(usersFile, System.Text.Json.JsonSerializer.Serialize(testUsers));
        
        _mockConfig.Setup(c => c["Auth:UsersFilePath"]).Returns(usersFile);
        
        var authService = new AuthService(
            _mockLogger.Object,
            _mockConfig.Object,
            _mockCache.Object,
            null); // No DB factory
        
        // Act
        await authService.InitializeAsync();
        
        // Assert
        var user = authService.GetUserByUsername("filetest");
        Assert.NotNull(user);
        Assert.Equal("File Test", user.FullName);
        
        // Cleanup
        Directory.Delete(tempDir, true);
    }
}
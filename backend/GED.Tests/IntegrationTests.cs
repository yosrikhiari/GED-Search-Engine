using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using GED.Core.Models;
using GED.Infrastructure.Services;
using Xunit;
using Moq;

namespace GED.Tests;

public class IntegrationTests
{
    [Fact]
    public void Test3_ConstructorDoesNotCreateUsersFile()
    {
        // Arrange - use a temp directory that doesn't exist
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var usersFilePath = Path.Combine(tempDir, "users.json");
        
        var mockLogger = new Mock<ILogger<AuthService>>();
        var mockConfig = new Mock<IConfiguration>();
        var mockCache = new Mock<IDistributedCache>();
        
        mockConfig.Setup(c => c["Auth:UsersFilePath"]).Returns(usersFilePath);
        mockConfig.Setup(c => c["Auth:SessionDurationHours"]).Returns("8");
        
        // Act - instantiate AuthService (constructor should NOT create any files)
        var authService = new AuthService(
            mockLogger.Object,
            mockConfig.Object,
            mockCache.Object,
            null);
        
        // Assert - no file should exist at this point
        var fileExistsAfterConstruction = File.Exists(usersFilePath);
        
        // Cleanup if it was created (which it shouldn't be)
        if (fileExistsAfterConstruction)
        {
            File.Delete(usersFilePath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir);
            
            Assert.Fail("File was created in constructor - this is a bug!");
        }
        
        // Now call InitializeAsync - this SHOULD create the file if needed
        // (but since no file exists and no DB factory, nothing happens)
        authService.InitializeAsync().Wait();
        
        // Still should not exist because no users to save
        var fileExistsAfterInit = File.Exists(usersFilePath);
        
        Console.WriteLine($"Test3 Result: File exists after constructor: {fileExistsAfterConstruction}");
        Console.WriteLine($"Test3 Result: File exists after InitializeAsync: {fileExistsAfterInit}");
        
        Assert.False(fileExistsAfterConstruction, "Constructor should NOT create users.json file");
        Assert.False(fileExistsAfterInit, "InitializeAsync should NOT create users.json file when no users exist");
    }
}
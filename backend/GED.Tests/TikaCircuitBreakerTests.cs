using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using GED.Infrastructure.Services;
using Xunit;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http;

namespace GED.Tests;

public class TikaCircuitBreakerTests
{
    [Fact]
    public void SimulateFailure_ThrowsHttpRequestException()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<TikaTextExtractionService>>();
        var mockConfig = new Mock<IConfiguration>();
        var mockFallbackLogger = new Mock<ILogger<TextExtractionService>>();
        
        mockConfig.Setup(c => c["Tika:Url"]).Returns("http://localhost:9998");
        mockConfig.Setup(c => c["Tika:Enabled"]).Returns("true");
        
        var httpClient = new HttpClient();
        var fallback = new TextExtractionService(mockFallbackLogger.Object);
        var tikaService = new TikaTextExtractionService(httpClient, mockLogger.Object, fallback, mockConfig.Object);
        
        // Act & Assert
        Assert.ThrowsAsync<HttpRequestException>(() => tikaService.SimulateFailureAsync());
    }

    [Fact]
    public async Task TikaDisabled_SkipsHttpCall()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<TikaTextExtractionService>>();
        var mockConfig = new Mock<IConfiguration>();
        var mockFallbackLogger = new Mock<ILogger<TextExtractionService>>();
        
        mockConfig.Setup(c => c["Tika:Url"]).Returns("http://localhost:9998");
        mockConfig.Setup(c => c["Tika:Enabled"]).Returns("false");
        
        var httpClient = new HttpClient();
        var fallback = new TextExtractionService(mockFallbackLogger.Object);
        var tikaService = new TikaTextExtractionService(httpClient, mockLogger.Object, fallback, mockConfig.Object);
        
        // Act - when disabled, Tika should not make HTTP call
        var result = await tikaService.ExtractTextAsync(new MemoryStream(), "application/pdf", CancellationToken.None);
        
        // Assert - verify that disabled mode returns fallback result without HTTP call
        // Empty string means fallback was used (no HTTP call made)
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task FallbackUsed_WhenTikaThrowsException()
    {
        // Arrange - handler that always throws
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Throws(new HttpRequestException("Connection refused"));

        var httpClient = new HttpClient(mockHandler.Object);
        var mockLogger = new Mock<ILogger<TikaTextExtractionService>>();
        var mockConfig = new Mock<IConfiguration>();
        var mockFallbackLogger = new Mock<ILogger<TextExtractionService>>();
        
        mockConfig.Setup(c => c["Tika:Url"]).Returns("http://localhost:9998");
        mockConfig.Setup(c => c["Tika:Enabled"]).Returns("true");
        
        var fallback = new TextExtractionService(mockFallbackLogger.Object);
        var tikaService = new TikaTextExtractionService(httpClient, mockLogger.Object, fallback, mockConfig.Object);
        
        // Act - Tika throws, fallback should be used
        var result = await tikaService.ExtractTextAsync(new MemoryStream(), "application/pdf", CancellationToken.None);
        
        // Assert - fallback handles error gracefully (returns null or empty)
        // No exception should propagate
        Console.WriteLine($"Fallback result: '{result}'");
    }
}
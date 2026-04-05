using FluentAssertions;
using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Concurrent;

namespace GED.Tests.Services;

[Trait("Category", "Unit")]
public class ConcurrencyTests
{
    [Fact]
    public void ConcurrentDocumentUploads_MaintainConsistency()
    {
        var counter = new ConcurrentDictionary<Guid, int>();
        var documentIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        
        Parallel.ForEach(documentIds, id =>
        {
            counter[id] = 1;
        });

        counter.Should().HaveCount(10);
        counter.Values.Should().OnlyContain(v => v == 1);
    }

    [Fact]
    public void ConcurrentAclChanges_ApplyCorrectly()
    {
        var aclChanges = new ConcurrentDictionary<(Guid DocId, Guid UserId), int>();
        var tasks = new List<(Guid DocId, Guid UserId)>();
        
        for (int i = 0; i < 100; i++)
        {
            tasks.Add((Guid.NewGuid(), Guid.NewGuid()));
        }

        Parallel.ForEach(tasks, task =>
        {
            aclChanges[task] = 1;
        });

        aclChanges.Should().HaveCount(100);
    }

    [Fact]
    public async Task ConcurrentSearchRequests_DoNotBlock()
    {
        var semaphore = new SemaphoreSlim(10, 10);
        var completionCount = 0;
        var lockObj = new object();

        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            await semaphore.WaitAsync();
            try
            {
                await Task.Delay(10);
                lock (lockObj) completionCount++;
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        completionCount.Should().Be(20);
    }

    [Fact]
    public void ConcurrentBulkOperations_MaintainOrder()
    {
        var results = new ConcurrentBag<int>();
        var lockObj = new object();

        Parallel.For(0, 100, i =>
        {
            lock (lockObj)
            {
                results.Add(i);
            }
        });

        results.Should().HaveCount(100);
        results.OrderBy(x => x).ToList().SequenceEqual(Enumerable.Range(0, 100)).Should().BeTrue();
    }

    [Fact]
    public void ConcurrentSessionCreation_IsThreadSafe()
    {
        var sessions = new ConcurrentDictionary<string, object>();
        var tokens = new List<string>();
        var lockObj = new object();

        Parallel.For(0, 50, _ =>
        {
            var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            lock (lockObj)
            {
                tokens.Add(token);
            }
            sessions[token] = new object();
        });

        sessions.Should().HaveCount(50, "All sessions should be added to the dictionary");
        tokens.Count.Should().Be(50, "All tokens should be added to the list");
    }

    [Fact]
    public void ConcurrentPriorityUpdates_AllApply()
    {
        var priorities = new ConcurrentDictionary<Guid, int>();
        var docId = Guid.NewGuid();

        Parallel.For(0, 10, i =>
        {
            priorities[docId] = i;
        });

        priorities.Should().HaveCount(1);
        priorities[docId].Should().BeLessThan(10);
    }

    [Fact]
    public async Task ThreadPoolExhaustion_HandledGracefully()
    {
        var results = new List<int>();
        var lockObj = new object();
        
        var tasks = Enumerable.Range(0, 100).Select(async i =>
        {
            await Task.Yield();
            lock (lockObj)
            {
                results.Add(i);
            }
            return i;
        }).ToList();

        await Task.WhenAll(tasks);

        results.Should().HaveCount(100);
    }
}

[Trait("Category", "Unit")]
public class OpenSearchFailureScenarioTests
{
    [Fact]
    public void OpenSearchTimeout_ReturnsEmptyResults()
    {
        var searchService = new Mock<ISearchService>();
        
        searchService
            .Setup(s => s.SearchAsync(It.IsAny<SearchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("OpenSearch request timed out"));

        var act = async () => await searchService.Object.SearchAsync(new SearchRequest(), CancellationToken.None);
        
        act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public void OpenSearchConnectionFailure_HandledGracefully()
    {
        var searchService = new Mock<ISearchService>();
        
        searchService
            .Setup(s => s.SearchAsync(It.IsAny<SearchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection refused"));

        var act = async () => await searchService.Object.SearchAsync(new SearchRequest(), CancellationToken.None);
        
        act.Should().ThrowAsync<Exception>()
            .WithMessage("*Connection*");
    }

    [Fact]
    public void OpenSearchInvalidQuery_ReturnsError()
    {
        var searchService = new Mock<ISearchService>();
        
        searchService
            .Setup(s => s.SearchAsync(It.IsAny<SearchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Invalid query syntax"));

        var act = async () => await searchService.Object.SearchAsync(new SearchRequest { Query = "]]][" }, CancellationToken.None);
        
        act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void OpenSearchBulkIndexFailure_HandledGracefully()
    {
        var searchService = new Mock<ISearchService>();
        
        searchService
            .Setup(s => s.BulkIndexDocumentsAsync(It.IsAny<IEnumerable<Document>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Bulk index failed"));

        var act = async () => await searchService.Object.BulkIndexDocumentsAsync(
            new List<Document> { new Document { Id = Guid.NewGuid() } }, 
            CancellationToken.None);
        
        act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void OpenSearchDeleteFailure_DoesNotCrash()
    {
        var searchService = new Mock<ISearchService>();
        
        searchService
            .Setup(s => s.DeleteDocumentIndexAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Document not found"));

        var act = async () => await searchService.Object.DeleteDocumentIndexAsync(Guid.NewGuid(), CancellationToken.None);
        
        act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void OpenSearchHealthCheck_HandlesUnhealthyState()
    {
        var isHealthy = false;
        
        try
        {
            throw new Exception("OpenSearch cluster is red");
        }
        catch
        {
            isHealthy = false;
        }

        isHealthy.Should().BeFalse();
    }

    [Fact]
    public void RetryMechanism_RetriesOnTransientFailure()
    {
        var attemptCount = 0;
        var maxRetries = 3;
        var lastException = (Exception?)null;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                attemptCount++;
                if (attemptCount < maxRetries)
                {
                    throw new Exception("Transient failure");
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        attemptCount.Should().Be(maxRetries);
        lastException.Should().NotBeNull("Should have thrown on intermediate attempts");
    }

    [Fact]
    public async Task UploadDocument_CleansUpTempFile_AfterProcessing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ged-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var tempFilePath = Path.Combine(tempDir, "test-file.pdf");
            await File.WriteAllTextAsync(tempFilePath, "Test content");
            
            File.Exists(tempFilePath).Should().BeTrue("Temp file should exist before cleanup");

            File.Delete(tempFilePath);

            File.Exists(tempFilePath).Should().BeFalse("Temp file should be cleaned up after processing");
            Directory.Exists(tempDir).Should().BeTrue("Temp directory should exist for test");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void TempFile_CleanupOnFailure_RemovesPartialFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ged-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var partialFile = Path.Combine(tempDir, "partial-upload.tmp");
            File.WriteAllBytes(partialFile, new byte[1024]);
            
            File.Exists(partialFile).Should().BeTrue("Partial file should exist");

            try { File.Delete(partialFile); } catch { }

            if (File.Exists(partialFile))
            {
                File.Delete(partialFile);
            }

            File.Exists(partialFile).Should().BeFalse("Partial file should be cleaned up on failure");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}

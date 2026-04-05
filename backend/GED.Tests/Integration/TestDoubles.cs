using GED.API.Services;
using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace GED.Tests.Integration;

public class NoOpRabbitMqConnectionService : IHostedService
{
    private readonly ILogger<NoOpRabbitMqConnectionService> _logger;
    
    public NoOpRabbitMqConnectionService(ILogger<NoOpRabbitMqConnectionService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("NoOp RabbitMQ connection established");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public class NoOpRabbitMqService : IMessageQueueService
{
    public Task PublishAsync<T>(string queueName, T message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task SubscribeAsync<T>(string queueName, Func<T, Task> handler, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public class NoOpRabbitMqStatusProvider : IRabbitMqStatusProvider
{
    public long GetQueueDepth() => 0;
}

public class InMemorySearchService : ISearchService
{
    private readonly ConcurrentDictionary<Guid, Document> _documents = new();

    public Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        var results = _documents.Values
            .Where(d => d.Title.Contains(request.Query, StringComparison.OrdinalIgnoreCase) ||
                       (d.Description?.Contains(request.Query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (d.ExtractedText?.Contains(request.Query, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(request.PageSize)
            .Select((d, i) => new DocumentSearchHit
            {
                Id = d.Id,
                Title = d.Title,
                Description = d.Description,
                Category = d.Category,
                FileName = d.FileName,
                ContentType = d.ContentType,
                Score = 1.0f,
                Highlights = new List<string>()
            })
            .ToList();

        return Task.FromResult(new SearchResult
        {
            TotalResults = results.Count,
            Documents = results,
            Page = 1,
            PageSize = request.PageSize,
            TotalPages = 1,
            SearchTimeMs = 0
        });
    }

    public Task<List<DocumentSuggestion>> GetRelatedDocumentsAsync(Guid documentId, int count = 5, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<DocumentSuggestion>());
    }

    public Task<NaturalLanguageQuery> ProcessNaturalLanguageQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new NaturalLanguageQuery
        {
            OriginalQuery = query,
            ProcessedQuery = query,
            Keywords = new List<string>(),
            Entities = new List<string>(),
            DetectedLanguage = "unknown",
            IsUnderstood = true
        });
    }

    public Task<bool> IndexDocumentAsync(Document document, CancellationToken cancellationToken = default)
    {
        _documents[document.Id] = document;
        return Task.FromResult(true);
    }

    public Task<bool> UpdateDocumentIndexAsync(Document document, CancellationToken cancellationToken = default)
    {
        _documents[document.Id] = document;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteDocumentIndexAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        _documents.TryRemove(documentId, out _);
        return Task.FromResult(true);
    }

    public Task<bool> BulkIndexDocumentsAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        foreach (var doc in documents)
        {
            _documents[doc.Id] = doc;
        }
        return Task.FromResult(true);
    }

    public Task IndexChunksAsync(Document document, List<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public class NoOpPipelineEventService : IPipelineEventService
{
    public Task EmitPipelineEventAsync(PipelineEvent evt)
    {
        return Task.CompletedTask;
    }
}

public class NoOpWebhookService : IWebhookService
{
    private readonly ILogger<NoOpWebhookService> _logger;

    public NoOpWebhookService(ILogger<NoOpWebhookService> logger)
    {
        _logger = logger;
    }

    public Task<List<WebhookConfig>> GetAllWebhooksAsync()
    {
        return Task.FromResult(new List<WebhookConfig>());
    }

    public Task<WebhookConfig?> GetWebhookByIdAsync(Guid id)
    {
        return Task.FromResult<WebhookConfig?>(null);
    }

    public Task<WebhookConfig> CreateWebhookAsync(WebhookConfig config)
    {
        config.Id = Guid.NewGuid();
        return Task.FromResult(config);
    }

    public Task<WebhookConfig?> UpdateWebhookAsync(Guid id, WebhookConfig config)
    {
        return Task.FromResult<WebhookConfig?>(null);
    }

    public Task<bool> DeleteWebhookAsync(Guid id)
    {
        return Task.FromResult(true);
    }

    public Task TriggerEventAsync(string eventName, object data, string? correlationId = null)
    {
        return Task.CompletedTask;
    }
}

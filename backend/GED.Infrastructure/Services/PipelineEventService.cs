using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Logging;
using OpenSearch.Client;
using System.Text.Json;

namespace GED.Infrastructure.Services;

public interface IPipelineEventService
{
    Task EmitPipelineEventAsync(PipelineEvent evt);
}

public class PipelineEventService : IPipelineEventService
{
    private readonly IOpenSearchClient _client;
    private readonly ILogger<PipelineEventService> _logger;
    private const string IndexName = "ged-pipeline-events";

    public PipelineEventService(IOpenSearchClient client, ILogger<PipelineEventService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task EmitPipelineEventAsync(PipelineEvent evt)
    {
        _logger.LogInformation("📊 Pipeline event: stage={Stage}, status={Status}, docId={DocId}, corrId={CorrId}",
            evt.PipelineStage, evt.Status, evt.DocumentId, evt.CorrelationId);
        try
        {
            await _client.IndexAsync(evt, i => i
                .Index(IndexName)
                .Id(Guid.NewGuid().ToString())
            );
            _logger.LogInformation("✅ Indexed pipeline event for {Stage}", evt.PipelineStage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit pipeline event for stage {Stage}, doc {DocId}",
                evt.PipelineStage, evt.DocumentId);
        }
    }
}

public static class PipelineEventEmitter
{
    public static async Task EmitAsync(
        IPipelineEventService? service,
        PipelineEvent evt)
    {
        if (service != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await service.EmitPipelineEventAsync(evt);
                }
                catch
                {
                }
            });
        }
    }
}
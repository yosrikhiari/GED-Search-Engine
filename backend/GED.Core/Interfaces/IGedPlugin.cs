using GED.Core.Models;

namespace GED.Core.Interfaces;

/// <summary>
/// Base interface for all GED plugins.
/// </summary>
public interface IGedPlugin
{
    /// <summary>Unique identifier for the plugin.</summary>
    string Id { get; }

    /// <summary>Human-readable name.</summary>
    string Name { get; }

    /// <summary>Plugin version.</summary>
    string Version { get; }

    /// <summary>Plugin description.</summary>
    string Description { get; }

    /// <summary>Plugin category (e.g., "ocr", "transformer", "action").</summary>
    string Category { get; }

    /// <summary>Whether the plugin is enabled by default.</summary>
    bool IsEnabledByDefault { get; }

    /// <summary>Initialize the plugin. Called once at startup.</summary>
    Task InitializeAsync();

    /// <summary>Shutdown the plugin. Called when application stops.</summary>
    Task ShutdownAsync();
}

/// <summary>
/// Plugin that processes documents during ingestion.
/// </summary>
public interface IDocumentIngestionPlugin : IGedPlugin
{
    /// <summary>Priority (lower = runs first).</summary>
    int Priority { get; }

    /// <summary>Process a document during ingestion pipeline.</summary>
    Task<DocumentProcessingResult> ProcessAsync(Document document, DocumentProcessingContext context, CancellationToken ct = default);
}

/// <summary>
/// Plugin that processes OCR output.
/// </summary>
public interface IOcrPostProcessingPlugin : IGedPlugin
{
    /// <summary>Priority (lower = runs first).</summary>
    int Priority { get; }

    /// <summary>Post-process OCR output.</summary>
    Task<string> ProcessOcrTextAsync(string ocrText, Document document, CancellationToken ct = default);
}

/// <summary>
/// Plugin that adds search capabilities.
/// </summary>
public interface ISearchPlugin : IGedPlugin
{
    /// <summary>Execute custom search logic.</summary>
    Task<SearchPluginResult> SearchAsync(SearchPluginRequest request, CancellationToken ct = default);
}

/// <summary>
/// Plugin that adds custom actions to documents.
/// </summary>
public interface IDocumentActionPlugin : IGedPlugin
{
    /// <summary>Execute the action.</summary>
    Task<DocumentActionResult> ExecuteAsync(DocumentActionRequest request, CancellationToken ct = default);

    /// <summary>Action display name.</summary>
    string DisplayName { get; }

    /// <summary>Icon to display in UI.</summary>
    string Icon { get; }
}

/// <summary>
/// Context passed to ingestion plugins.
/// </summary>
public class DocumentProcessingContext
{
    public required Dictionary<string, object> Metadata { get; init; }
    public required List<string> Warnings { get; init; }
    public required List<string> Errors { get; init; }
}

/// <summary>
/// Result of document processing.
/// </summary>
public class DocumentProcessingResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object>? UpdatedMetadata { get; set; }
    public string? UpdatedText { get; set; }
}

/// <summary>
/// Request for search plugins.
/// </summary>
public class SearchPluginRequest
{
    public required string Query { get; init; }
    public int TopK { get; init; } = 10;
    public List<string>? Categories { get; init; }
}

/// <summary>
/// Result from search plugins.
/// </summary>
public class SearchPluginResult
{
    public List<SearchPluginHit> Hits { get; set; } = new();
}

public class SearchPluginHit
{
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public float Score { get; set; }
    public string? Excerpt { get; set; }
}

/// <summary>
/// Request for document action plugins.
/// </summary>
public class DocumentActionRequest
{
    public required Guid DocumentId { get; init; }
    public required string ActionName { get; init; }
    public Dictionary<string, object>? Parameters { get; init; }
}

/// <summary>
/// Result from document action plugins.
/// </summary>
public class DocumentActionResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, object>? Data { get; set; }
}

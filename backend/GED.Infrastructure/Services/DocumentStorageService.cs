using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace GED.Infrastructure.Services;

public class DocumentStorageService : IDocumentStorageService
{
    private readonly ILogger<DocumentStorageService> _logger;
    private readonly IPipelineEventService? _pipelineEventService;
    private readonly string _basePath;
    private readonly string _tempPath;

    public string BasePath => _basePath;
    public string TempPath => _tempPath;

    public DocumentStorageService(
        ILogger<DocumentStorageService> logger,
        IConfigurationService? config = null,
        IPipelineEventService? pipelineEventService = null)
    {
        _logger = logger;
        _pipelineEventService = pipelineEventService;
        _basePath = config?["Document:StoragePath"] ?? "/var/lib/ged/documents";
        _tempPath = Path.Combine(Path.GetTempPath(), "ged-uploads");
        Directory.CreateDirectory(_basePath);
        Directory.CreateDirectory(_tempPath);
        _logger.LogInformation("DocumentStorageService initialized: base={BasePath}, temp={TempPath}", _basePath, _tempPath);
    }

    public async Task<StoredFile> StageFileAsync(
        Stream fileStream, 
        Guid documentId, 
        string fileExtension, 
        CancellationToken ct = default)
    {
        return await StageFileAsync(fileStream, documentId, fileExtension, null, ct);
    }

    public async Task<StoredFile> StageFileAsync(
        Stream fileStream, 
        Guid documentId, 
        string fileExtension, 
        string? correlationId,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var storedFileName = $"{documentId}{fileExtension}";
        var finalPath = Path.Combine(_basePath, storedFileName);
        var tempPath = Path.Combine(_tempPath, $"{documentId}{fileExtension}");

        if (_pipelineEventService != null && !string.IsNullOrEmpty(correlationId))
        {
            _ = Task.Run(() => _pipelineEventService.EmitPipelineEventAsync(new PipelineEvent
            {
                PipelineStage = "file_storage",
                DocumentId = documentId.ToString(),
                CorrelationId = correlationId ?? string.Empty,
                Status = "started",
                DurationMs = stopwatch.ElapsedMilliseconds,
                ServiceName = nameof(DocumentStorageService),
                FileName = fileExtension,
                FileHash = "",
                FileSizeBytes = 0
            }));
        }

        try
        {
            fileStream.Position = 0;
            var (savedPath, fileHash) = await fileStream.WriteToFileWithHashAsync(tempPath, ct);
            
            var fileInfo = new FileInfo(savedPath);
            
            _logger.LogInformation("Document {DocumentId} staged to temp {Path}, hash: {Hash}", 
                documentId, savedPath, fileHash);

            stopwatch.Stop();

            if (_pipelineEventService != null && !string.IsNullOrEmpty(correlationId))
            {
                _ = Task.Run(() => _pipelineEventService.EmitPipelineEventAsync(new PipelineEvent
                {
                    PipelineStage = "file_storage",
                    DocumentId = documentId.ToString(),
                    CorrelationId = correlationId,
                    Status = "completed",
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    ServiceName = nameof(DocumentStorageService),
                    FileSizeBytes = fileInfo.Length,
                    FileHash = fileHash,
                    DuplicateDetected = false
                }));
            }

            return new StoredFile
            {
                FinalPath = finalPath,
                TempPath = tempPath,
                FileHash = fileHash,
                FileSize = fileInfo.Length
            };
        }
        catch (Exception ex)
        {
            if (_pipelineEventService != null && !string.IsNullOrEmpty(correlationId))
            {
                _ = Task.Run(() => _pipelineEventService.EmitPipelineEventAsync(new PipelineEvent
                {
                    PipelineStage = "file_storage",
                    DocumentId = documentId.ToString(),
                    CorrelationId = correlationId,
                    Status = "failed",
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    ServiceName = nameof(DocumentStorageService),
                    ErrorType = ex.GetType().Name,
                    ErrorMessage = ex.Message
                }));
            }
            throw;
        }
    }

    public async Task FinalizeFileAsync(string tempPath, string finalPath, CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Move(tempPath, finalPath, overwrite: true);
                _logger.LogInformation("File moved from temp to final location: {Temp} -> {Final}", tempPath, finalPath);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to move file from temp to final location: {TempPath}", tempPath);
            throw;
        }
    }

    public Task CleanupTempFileAsync(string tempPath, CancellationToken ct = default)
    {
        try
        {
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
            {
                File.Delete(tempPath);
                _logger.LogInformation("Cleaned up temp file: {Path}", tempPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up temp file: {Path}", tempPath);
        }
        return Task.CompletedTask;
    }

    public Task<bool> DeleteFileAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("File deleted: {Path}", filePath);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file: {Path}", filePath);
            return Task.FromResult(false);
        }
    }

    public Task<Stream> GetFileStreamAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");
        return Task.FromResult<Stream>(File.OpenRead(filePath));
    }
}

public interface IConfigurationService
{
    string? this[string key] { get; }
}

public class ConfigurationService : IConfigurationService
{
    private readonly IConfiguration _config;
    
    public ConfigurationService(IConfiguration config)
    {
        _config = config;
    }
    
    public string? this[string key] => _config[key];
}

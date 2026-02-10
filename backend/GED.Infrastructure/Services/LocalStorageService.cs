using GED.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

public class LocalStorageService : IStorageService
{
    private readonly ILogger<LocalStorageService> _logger;
    private readonly string _basePath;

    public LocalStorageService(ILogger<LocalStorageService> logger, string basePath = "/var/lib/ged/storage")
    {
        _logger = logger;
        _basePath = basePath;
        Directory.CreateDirectory(_basePath);
    }

    public async Task<string> StoreFileAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = Path.Combine(_basePath, fileName);
            var directory = Path.GetDirectoryName(filePath);
            
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var fileStreamOutput = File.Create(filePath);
            await fileStream.CopyToAsync(fileStreamOutput, cancellationToken);

            _logger.LogInformation("File stored successfully: {FilePath}", filePath);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing file {FileName}", fileName);
            throw;
        }
    }

    public Task<Stream> RetrieveFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            Stream stream = File.OpenRead(filePath);
            return Task.FromResult(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving file {FilePath}", filePath);
            throw;
        }
    }

    public Task<bool> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("File deleted successfully: {FilePath}", filePath);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FilePath}", filePath);
            return Task.FromResult(false);
        }
    }

    public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(File.Exists(filePath));
    }

    public Task<long> GetFileSizeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return Task.FromResult(0L);
            }

            var fileInfo = new FileInfo(filePath);
            return Task.FromResult(fileInfo.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file size {FilePath}", filePath);
            return Task.FromResult(0L);
        }
    }
}
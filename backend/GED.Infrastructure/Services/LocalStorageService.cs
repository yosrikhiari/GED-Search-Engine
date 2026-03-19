using GED.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

/// <summary>
/// Local filesystem-based storage service for document files.
/// 
/// <para>
/// Files are stored at a configurable base path with their original filenames preserved.
/// This implementation is suitable for development and small deployments.
/// For production, consider using cloud storage (S3, Azure Blob, etc.) for scalability and durability.
/// </para>
/// 
/// <para>
/// Thread-safety: This implementation handles concurrent file operations safely using
/// the underlying filesystem's locking mechanisms.
/// </para>
/// </summary>
public class LocalStorageService : IStorageService
{
    private readonly ILogger<LocalStorageService> _logger;

    /// <summary>
    /// Base directory path where files are stored.
    /// </summary>
    private readonly string _basePath;

    /// <summary>
    /// Initializes a new instance of <see cref="LocalStorageService"/>.
    /// </summary>
    /// <param name="logger">Logger for storage events.</param>
    /// <param name="basePath">
    ///   Base directory path for file storage.
    ///   Defaults to "/var/lib/ged/storage" if not specified.
    /// </param>
    public LocalStorageService(ILogger<LocalStorageService> logger, string basePath = "/var/lib/ged/storage")
    {
        _logger = logger;
        _basePath = basePath;
        Directory.CreateDirectory(_basePath);
    }

    /// <inheritdoc />
    public async Task<string> StoreFileAsync(
        Stream fileStream, 
        string fileName, 
        string contentType, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = Path.Combine(_basePath, fileName);
            var directory = Path.GetDirectoryName(filePath);
            
            // Ensure directory exists before creating file
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

    /// <inheritdoc />
    public Task<Stream> RetrieveFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            // Return open file stream for reading
            Stream stream = File.OpenRead(filePath);
            return Task.FromResult(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving file {FilePath}", filePath);
            throw;
        }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(File.Exists(filePath));
    }

    /// <inheritdoc />
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

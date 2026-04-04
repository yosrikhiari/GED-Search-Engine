namespace GED.Core.Interfaces;

public class StoredFile
{
    public required string FinalPath { get; init; }
    public required string TempPath { get; init; }
    public required string FileHash { get; init; }
    public required long FileSize { get; init; }
}

public interface IDocumentStorageService
{
    string BasePath { get; }
    string TempPath { get; }
    Task<StoredFile> StageFileAsync(Stream fileStream, Guid documentId, string fileExtension, CancellationToken ct = default);
    Task FinalizeFileAsync(string tempPath, string finalPath, CancellationToken ct = default);
    Task CleanupTempFileAsync(string tempPath, CancellationToken ct = default);
    Task<bool> DeleteFileAsync(string filePath, CancellationToken ct = default);
    Task<Stream> GetFileStreamAsync(string filePath, CancellationToken ct = default);
}

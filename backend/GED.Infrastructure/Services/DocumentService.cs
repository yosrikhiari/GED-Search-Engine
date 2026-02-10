using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

public class DocumentService : IDocumentService
{
    private readonly ILogger<DocumentService> _logger;
    private readonly IStorageService _storageService;
    private readonly ITextExtractionService _textExtractionService;
    private readonly string _basePath;

    public DocumentService(
        ILogger<DocumentService> logger,
        IStorageService storageService,
        ITextExtractionService textExtractionService)
    {
        _logger = logger;
        _storageService = storageService;
        _textExtractionService = textExtractionService;
        _basePath = "/var/lib/ged/documents";
        Directory.CreateDirectory(_basePath);
    }

    public async Task<Document?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var metadataPath = Path.Combine(_basePath, $"{id}.json");
            if (!File.Exists(metadataPath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            return System.Text.Json.JsonSerializer.Deserialize<Document>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document {DocumentId}", id);
            return null;
        }
    }

    public async Task<Document> UploadDocumentAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        string? title = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var documentId = Guid.NewGuid();
            var fileExtension = Path.GetExtension(fileName);
            var storedFileName = $"{documentId}{fileExtension}";
            var filePath = Path.Combine(_basePath, storedFileName);

            // Save file
            using (var fileStreamOutput = File.Create(filePath))
            {
                await fileStream.CopyToAsync(fileStreamOutput, cancellationToken);
            }

            var fileInfo = new FileInfo(filePath);

            // Extract text if possible
            string? extractedText = null;
            try
            {
                fileStream.Position = 0;
                extractedText = await _textExtractionService.ExtractTextAsync(fileStream, contentType, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not extract text from document");
            }

            var document = new Document
            {
                Id = documentId,
                Title = title ?? fileName,
                FileName = fileName,
                FilePath = filePath,
                ContentType = contentType,
                FileSize = fileInfo.Length,
                CreatedAt = DateTime.UtcNow,
                Status = DocumentStatus.Indexed,  // ✅ Set to Indexed
                ExtractedText = extractedText,
                Metadata = metadata,
                Category = metadata?.ContainsKey("category") == true ? 
                    metadata["category"]?.ToString() : null,
                Version = 1
            };

            // Save metadata
            var metadataPath = Path.Combine(_basePath, $"{documentId}.json");
            var json = System.Text.Json.JsonSerializer.Serialize(document);
            await File.WriteAllTextAsync(metadataPath, json, cancellationToken);
            _logger.LogInformation(
                "Document created: ID={Id}, Title={Title}, Category={Category}, ExtractedText length={Length}",
                document.Id, document.Title, document.Category, 
                document.ExtractedText?.Length ?? 0
            );
            _logger.LogInformation("Document {DocumentId} uploaded successfully", documentId);
            return document;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading document");
            throw;
        }
    }

    public async Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var document = await GetDocumentByIdAsync(id, cancellationToken);
            if (document == null)
            {
                return false;
            }

            // Delete file
            if (File.Exists(document.FilePath))
            {
                File.Delete(document.FilePath);
            }

            // Delete metadata
            var metadataPath = Path.Combine(_basePath, $"{id}.json");
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }

            _logger.LogInformation("Document {DocumentId} deleted successfully", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document {DocumentId}", id);
            return false;
        }
    }

    public async Task<Document> UpdateDocumentAsync(Guid id, Document document, CancellationToken cancellationToken = default)
    {
        try
        {
            document.ModifiedAt = DateTime.UtcNow;
            
            var metadataPath = Path.Combine(_basePath, $"{id}.json");
            var json = System.Text.Json.JsonSerializer.Serialize(document);
            await File.WriteAllTextAsync(metadataPath, json, cancellationToken);

            _logger.LogInformation("Document {DocumentId} updated successfully", id);
            return document;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating document {DocumentId}", id);
            throw;
        }
    }

    public async Task<Stream> GetDocumentContentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var document = await GetDocumentByIdAsync(id, cancellationToken);
            if (document == null)
            {
                throw new FileNotFoundException($"Document {id} not found");
            }

            if (!File.Exists(document.FilePath))
            {
                throw new FileNotFoundException($"File not found: {document.FilePath}");
            }

            return File.OpenRead(document.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document content {DocumentId}", id);
            throw;
        }
    }

    public async Task<List<Document>> GetDocumentsByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var documents = new List<Document>();
        foreach (var id in ids)
        {
            var doc = await GetDocumentByIdAsync(id, cancellationToken);
            if (doc != null)
            {
                documents.Add(doc);
            }
        }
        return documents;
    }
}
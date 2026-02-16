using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

public class DocumentService : IDocumentService
{
    private readonly ILogger<DocumentService> _logger;
    private readonly IStorageService _storageService;
    private readonly ITextExtractionService _textExtractionService;
    private readonly DocumentDateExtractor? _dateExtractor; // ⭐ NEW
    private readonly string _basePath;

    public DocumentService(
        ILogger<DocumentService> logger,
        IStorageService storageService,
        ITextExtractionService textExtractionService,
        DocumentDateExtractor? dateExtractor = null) // ⭐ OPTIONAL - graceful degradation
    {
        _logger = logger;
        _storageService = storageService;
        _textExtractionService = textExtractionService;
        _dateExtractor = dateExtractor; // ⭐ NEW
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

        // ⭐ NEW: Extract document date using LLM
        DateTime? documentDate = null;
        if (_dateExtractor != null && !string.IsNullOrWhiteSpace(extractedText))
        {
            var category = metadata?.ContainsKey("category") == true ? 
                metadata["category"]?.ToString() : "Other";
            
            try
            {
                _logger.LogInformation("🗓️ Attempting to extract document date from content...");
                
                var dateInfo = await _dateExtractor.ExtractDocumentDateAsync(
                    extractedText,
                    fileName,
                    category ?? "Other",
                    cancellationToken
                );

                if (dateInfo?.DocumentDate != null && dateInfo.Confidence > 0.5f)
                {
                    documentDate = dateInfo.DocumentDate.Value;
                    
                    // Store in metadata for debugging
                    metadata ??= new Dictionary<string, object>();
                    metadata["extracted_date"] = documentDate.Value.ToString("yyyy-MM-dd");
                    metadata["date_confidence"] = dateInfo.Confidence;
                    metadata["date_type"] = dateInfo.DateType;
                    
                    _logger.LogInformation(
                        "✅ Document date extracted: {Date} (confidence: {Confidence:F2}, type: {Type})",
                        documentDate.Value.ToString("yyyy-MM-dd"),
                        dateInfo.Confidence,
                        dateInfo.DateType
                    );
                }
                else
                {
                    _logger.LogWarning(
                        "❌ Document date extraction confidence too low or no date found (confidence: {Confidence:F2})",
                        dateInfo?.Confidence ?? 0
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract document date");
            }
        }
        else
        {
            if (_dateExtractor == null)
            {
                _logger.LogWarning("⚠️ DocumentDateExtractor is not available (null)");
            }
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                _logger.LogWarning("⚠️ No text extracted from document, cannot extract date");
            }
        }

        // ⭐ CRITICAL CHANGE: CreatedAt is ALWAYS upload time, DocumentDate is from content
        var uploadTime = DateTime.UtcNow;

        var document = new Document
        {
            Id = documentId,
            Title = title ?? fileName,
            FileName = fileName,
            FilePath = filePath,
            ContentType = contentType,
            FileSize = fileInfo.Length,
            CreatedAt = uploadTime,  // ⭐ ALWAYS upload time
            DocumentDate = documentDate,  // ⭐ NEW: Extracted date (can be null)
            Status = DocumentStatus.Indexed,
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
            "📄 Document created: ID={Id}, Title={Title}, Category={Category}, " +
            "UploadDate={UploadDate}, DocumentDate={DocumentDate}, ExtractedTextLength={Length}",
            document.Id, 
            document.Title, 
            document.Category, 
            uploadTime.ToString("yyyy-MM-dd HH:mm:ss"),
            documentDate?.ToString("yyyy-MM-dd") ?? "NOT_EXTRACTED",
            document.ExtractedText?.Length ?? 0
        );
        
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
using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace GED.Infrastructure.Services;

/// <summary>
/// DocumentService backed by PostgreSQL via EF Core.
/// Replaces the previous flat JSON file implementation.
/// </summary>
public class DocumentService : IDocumentService
{
    private readonly ILogger<DocumentService> _logger;
    private readonly IStorageService _storageService;
    private readonly ITextExtractionService _textExtractionService;
    private readonly DocumentDateExtractor? _dateExtractor;
    private readonly GedDbContext _db;
    private readonly string _basePath;

    public DocumentService(
        ILogger<DocumentService> logger,
        IStorageService storageService,
        ITextExtractionService textExtractionService,
        GedDbContext db,
        DocumentDateExtractor? dateExtractor = null)
    {
        _logger = logger;
        _storageService = storageService;
        _textExtractionService = textExtractionService;
        _db = db;
        _dateExtractor = dateExtractor;
        _basePath = "/var/lib/ged/documents";
        Directory.CreateDirectory(_basePath);
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    public async Task<Document?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await _db.Documents
                .Include(d => d.DocumentMetadata)
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

            return entity == null ? null : MapToDomain(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document {DocumentId}", id);
            return null;
        }
    }

    public async Task<List<Document>> GetDocumentsByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var idList = ids.ToList();
            var entities = await _db.Documents
                .Include(d => d.DocumentMetadata)
                .AsNoTracking()
                .Where(d => idList.Contains(d.Id))
                .ToListAsync(cancellationToken);

            return entities.Select(MapToDomain).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting documents by IDs");
            return new List<Document>();
        }
    }

    // ── Upload ────────────────────────────────────────────────────────────────

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

            // ── 1. Save file & compute hash ──────────────────────────────────
            byte[] fileBytes;
            using (var ms = new MemoryStream())
            {
                await fileStream.CopyToAsync(ms, cancellationToken);
                fileBytes = ms.ToArray();
            }
            await File.WriteAllBytesAsync(filePath, fileBytes, cancellationToken);

            var fileInfo = new FileInfo(filePath);
            var fileHash = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLower();

            // ── 2. Extract text ──────────────────────────────────────────────
            string? extractedText = null;
            try
            {
                using var textStream = new MemoryStream(fileBytes);
                extractedText = await _textExtractionService.ExtractTextAsync(
                    textStream, contentType, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not extract text from document");
            }

            // ── 3. Generate description & tags ───────────────────────────────
            var description = GenerateDescription(extractedText, fileName);
            var category = metadata?.ContainsKey("category") == true
                ? metadata["category"]?.ToString()
                : null;
            var tags = GenerateTags(fileName, category, extractedText);

            // ── 4. Extract document date via LLM ─────────────────────────────
            DateTime? documentDate = null;
            if (_dateExtractor != null && !string.IsNullOrWhiteSpace(extractedText))
            {
                try
                {
                    _logger.LogInformation("🗓️ Attempting to extract document date...");
                    var dateInfo = await _dateExtractor.ExtractDocumentDateAsync(
                        extractedText, fileName, category ?? "Other", cancellationToken);

                    if (dateInfo?.DocumentDate != null && dateInfo.Confidence > 0.5f)
                    {
                        documentDate = dateInfo.DocumentDate.Value;
                        metadata ??= new Dictionary<string, object>();
                        metadata["extracted_date"] = documentDate.Value.ToString("yyyy-MM-dd");
                        metadata["date_confidence"] = dateInfo.Confidence;
                        metadata["date_type"] = dateInfo.DateType;

                        _logger.LogInformation(
                            "✅ Document date extracted: {Date} (confidence: {Confidence:F2}, type: {Type})",
                            documentDate.Value.ToString("yyyy-MM-dd"), dateInfo.Confidence, dateInfo.DateType);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract document date");
                }
            }

            // ── 5. Build & persist EF entity ─────────────────────────────────
            var uploadTime = DateTime.UtcNow;

            var entity = new DocumentEntity
            {
                Id            = documentId,
                Title         = title ?? Path.GetFileNameWithoutExtension(fileName),
                Description   = description,
                FileName      = fileName,
                FilePath      = filePath,
                ContentType   = contentType,
                FileSize      = fileInfo.Length,
                FileHash      = fileHash,
                CreatedAt     = uploadTime,
                CreatedBy     = "system",
                ModifiedAt    = uploadTime,
                ModifiedBy    = "system",
                DocumentDate  = documentDate,
                Status        = DocumentStatus.Indexed,
                IsOcrProcessed = false,
                ExtractedText = extractedText,
                Metadata      = metadata,
                Tags          = tags,
                Category      = category,
                Version       = 1,
                DocumentMetadata = BuildMetadataEntities(documentId, metadata, uploadTime)
            };

            _db.Documents.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "📄 Document persisted to DB: ID={Id}, Title={Title}, Category={Category}, DocumentDate={DocumentDate}",
                entity.Id, entity.Title, entity.Category,
                documentDate?.ToString("yyyy-MM-dd") ?? "NOT_EXTRACTED");

            return MapToDomain(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading document");
            throw;
        }
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public async Task<Document> UpdateDocumentAsync(
        Guid id,
        Document document,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await _db.Documents.FindAsync(new object[] { id }, cancellationToken)
                ?? throw new KeyNotFoundException($"Document {id} not found");

            entity.Title       = document.Title;
            entity.Description = document.Description;
            entity.Category    = document.Category;
            entity.Tags        = document.Tags;
            entity.Metadata    = document.Metadata;
            entity.ModifiedAt  = DateTime.UtcNow;
            entity.ModifiedBy  = "system";

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Document {DocumentId} updated", id);
            return MapToDomain(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating document {DocumentId}", id);
            throw;
        }
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    public async Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await _db.Documents.FindAsync(new object[] { id }, cancellationToken);
            if (entity == null) return false;

            // Delete physical file
            if (File.Exists(entity.FilePath))
                File.Delete(entity.FilePath);

            _db.Documents.Remove(entity);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Document {DocumentId} deleted", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document {DocumentId}", id);
            return false;
        }
    }

    // ── File content ──────────────────────────────────────────────────────────

    public async Task<Stream> GetDocumentContentAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var document = await GetDocumentByIdAsync(id, cancellationToken)
            ?? throw new FileNotFoundException($"Document {id} not found");

        if (!File.Exists(document.FilePath))
            throw new FileNotFoundException($"File not found: {document.FilePath}");

        return File.OpenRead(document.FilePath);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static Document MapToDomain(DocumentEntity e) => new()
    {
        Id               = e.Id,
        Title            = e.Title,
        Description      = e.Description,
        FileName         = e.FileName,
        FilePath         = e.FilePath,
        ContentType      = e.ContentType,
        FileSize         = e.FileSize,
        FileHash         = e.FileHash,
        CreatedAt        = e.CreatedAt,
        DocumentDate     = e.DocumentDate,
        ModifiedAt       = e.ModifiedAt,
        CreatedBy        = e.CreatedBy,
        ModifiedBy       = e.ModifiedBy,
        Status           = e.Status,
        IsOcrProcessed   = e.IsOcrProcessed,
        OcrText          = e.OcrText,
        ExtractedText    = e.ExtractedText,
        Tags             = e.Tags,
        Category         = e.Category,
        Metadata         = e.Metadata,
        Version          = e.Version,
        ParentDocumentId = e.ParentDocumentId,
        DocumentMetadata = e.DocumentMetadata
            .Select(m => new DocumentMetadata
            {
                Id         = m.Id,
                DocumentId = m.DocumentId,
                Key        = m.Key,
                Value      = m.Value,
                Type       = m.Type,
                CreatedAt  = m.CreatedAt
            }).ToList()
    };

    private static List<DocumentMetadataEntity> BuildMetadataEntities(
        Guid documentId,
        Dictionary<string, object>? metadata,
        DateTime createdAt)
    {
        if (metadata == null) return new List<DocumentMetadataEntity>();

        return metadata
            .Where(kvp => kvp.Value != null)
            .Select(kvp => new DocumentMetadataEntity
            {
                Id         = Guid.NewGuid(),
                DocumentId = documentId,
                Key        = kvp.Key,
                Value      = kvp.Value.ToString(),
                Type       = kvp.Value switch
                {
                    bool   => MetadataType.Boolean,
                    int or long or float or double => MetadataType.Number,
                    DateTime => MetadataType.Date,
                    _ => MetadataType.String
                },
                CreatedAt = createdAt
            })
            .ToList();
    }

    // ── Text/tag helpers (unchanged from original) ────────────────────────────

    private static string GenerateDescription(string? extractedText, string fileName)
    {
        if (string.IsNullOrWhiteSpace(extractedText))
            return $"Document: {Path.GetFileNameWithoutExtension(fileName)}";

        var lines = extractedText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 15)
            .Take(3)
            .ToList();

        if (!lines.Any())
            return $"Document: {Path.GetFileNameWithoutExtension(fileName)}";

        var description = string.Join(" ", lines);
        return description.Length > 200 ? description[..197] + "..." : description;
    }

    private static List<string> GenerateTags(
        string fileName, string? category, string? extractedText)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(category)) tags.Add(category.ToLower());

        foreach (var part in Regex.Split(
                     Path.GetFileNameWithoutExtension(fileName), @"[\s_\-]+")
                 .Where(p => p.Length > 3).Select(p => p.ToLower()))
            tags.Add(part);

        var ext = Path.GetExtension(fileName).TrimStart('.').ToLower();
        if (!string.IsNullOrWhiteSpace(ext)) tags.Add(ext);

        if (!string.IsNullOrWhiteSpace(extractedText))
        {
            var keywords = new[]
            {
                "invoice", "contract", "agreement", "report", "proposal",
                "confidential", "draft", "final", "signed", "approved",
                "budget", "payment", "license", "legal", "nda"
            };
            var lower = extractedText.ToLower();
            foreach (var kw in keywords)
                if (lower.Contains(kw)) tags.Add(kw);

            var yearMatch = Regex.Match(extractedText, @"\b(20\d{2})\b");
            if (yearMatch.Success) tags.Add(yearMatch.Value);
        }

        return tags.Where(t => t.Length > 2).OrderBy(t => t).Take(15).ToList();
    }
}
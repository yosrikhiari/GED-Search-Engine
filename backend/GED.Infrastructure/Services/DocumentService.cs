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
    private readonly DocumentIngestionPipeline _ingestionPipeline;  // ✅ Already injected

    private readonly GedDbContext _db;
    private readonly string _basePath;

    public DocumentService(
        ILogger<DocumentService> logger,
        IStorageService storageService,
        ITextExtractionService textExtractionService,
        GedDbContext db,
        DocumentIngestionPipeline ingestionPipeline,  // ✅ Already in constructor
        DocumentDateExtractor? dateExtractor = null)
    {
        _logger = logger;
        _storageService = storageService;
        _textExtractionService = textExtractionService;
        _ingestionPipeline = ingestionPipeline;  // ✅ Already assigned
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

            // ── 2. RUN ALL ENRICHMENT STEPS VIA PIPELINE ─────────────────────
            // This replaces lines 90-160+ of mixed concerns (text extraction,
            // description generation, tags, date extraction)
            var category = metadata?.GetValueOrDefault("category")?.ToString();
            var ingestion = await _ingestionPipeline.RunAsync(
                fileBytes, fileName, contentType, category, cancellationToken);

            // ── 3. MERGE PIPELINE METADATA WITH CALLER-SUPPLIED METADATA ─────
            var mergedMetadata = metadata ?? new Dictionary<string, object>();
            foreach (var kv in ingestion.Metadata)
                mergedMetadata[kv.Key] = kv.Value;

            // ── 4. EXTRACT DOCUMENT DATE (optional fallback if pipeline doesn't do it) ──
            DateTime? documentDate = ingestion.DocumentDate;
            
            // Optional: If pipeline doesn't extract date, try legacy extractor
            if (documentDate == null && _dateExtractor != null && !string.IsNullOrWhiteSpace(ingestion.ExtractedText))
            {
                try
                {
                    _logger.LogInformation("🗓️ Attempting to extract document date via legacy extractor...");
                    var dateInfo = await _dateExtractor.ExtractDocumentDateAsync(
                        ingestion.ExtractedText, fileName, category ?? "Other", cancellationToken);

                    if (dateInfo?.DocumentDate != null && dateInfo.Confidence > 0.5f)
                    {
                        documentDate = DateTime.SpecifyKind(dateInfo.DocumentDate.Value, DateTimeKind.Utc);
                        mergedMetadata["extracted_date"] = documentDate.Value.ToString("yyyy-MM-dd");
                        mergedMetadata["date_confidence"] = dateInfo.Confidence;
                        mergedMetadata["date_type"] = dateInfo.DateType;

                        _logger.LogInformation(
                            "✅ Document date extracted: {Date} (confidence: {Confidence:F2}, type: {Type})",
                            documentDate.Value.ToString("yyyy-MM-dd"), dateInfo.Confidence, dateInfo.DateType);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract document date via legacy extractor");
                }
            }

            // ── 5. Build & persist EF entity ─────────────────────────────────
            var uploadTime = DateTime.UtcNow;

            var entity = new DocumentEntity
            {
                Id            = documentId,
                Title         = title ?? Path.GetFileNameWithoutExtension(fileName),
                Description   = ingestion.Description ?? GenerateDescription(ingestion.ExtractedText, fileName),
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
                IsOcrProcessed = !string.IsNullOrWhiteSpace(ingestion.ExtractedText),
                ExtractedText = ingestion.ExtractedText,
                Metadata      = mergedMetadata,
                Tags          = ingestion.Tags ?? GenerateTags(fileName, category, ingestion.ExtractedText),
                Category      = category,
                Version       = 1,
                DocumentMetadata = BuildMetadataEntities(documentId, mergedMetadata, uploadTime)
            };

            _db.Documents.Add(entity);

            // Queue OCR job via outbox pattern (same transaction = atomic)
            bool needsOcr = contentType.StartsWith("image/") || contentType == "application/pdf";
            if (needsOcr)
            {
                var outboxMessage = new OutboxMessage
                {
                    Id        = Guid.NewGuid(),
                    Type      = "OcrJob",
                    Payload   = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        JobId      = Guid.NewGuid(),
                        DocumentId = documentId,
                        Language   = "eng+fra+ara"
                    }),
                    CreatedAt  = uploadTime,
                    RetryCount = 0
                };
                _db.OutboxMessages.Add(outboxMessage);
                _logger.LogInformation("📬 Outbox OCR job queued for document {Id}", documentId);
            }

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

    // ── Text/tag helpers (fallbacks if pipeline doesn't provide them) ─────────

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
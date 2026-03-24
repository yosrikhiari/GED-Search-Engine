using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace GED.Infrastructure.Services;

/// <summary>
/// Core document management service backed by PostgreSQL via Entity Framework Core.
/// 
/// <para>
/// Responsibilities:
/// <list type="bullet">
///   <item>
///     <term>CRUD Operations</term>
///     <description>
///       Upload, read, update, and delete documents with associated metadata.
///     </description>
///   </item>
///   <item>
///     <term>File Storage</term>
///     <description>
///       Persists uploaded files to the local filesystem with content-addressable naming.
///     </description>
///   </item>
///   <item>
///     <term>Text Enrichment</term>
///     <description>
///       Delegates text extraction, description generation, and tag creation to
///       <see cref="DocumentIngestionPipeline"/> for separation of concerns.
///     </description>
///   </item>
///   <item>
///     <term>OCR Job Queuing</term>
///     <description>
///       Uses the outbox pattern to reliably queue OCR jobs to RabbitMQ within
///       the same transaction as document creation (atomic operation).
///     </description>
///   </item>
/// </list>
/// </para>
/// 
/// <para>
/// File naming: Files are stored with UUID-based names to prevent conflicts and
/// enable content-addressable retrieval. The original filename is preserved in metadata.
/// </para>
/// </summary>
public class DocumentService : IDocumentService
{
    private readonly ILogger<DocumentService> _logger;
    private readonly IStorageService _storageService;
    private readonly ITextExtractionService _textExtractionService;
    private readonly DocumentDateExtractor? _dateExtractor;

    /// <summary>
    /// Handles all enrichment steps (text extraction, description, tags).
    /// Separated from persistence for testability and single responsibility.
    /// </summary>
    private readonly DocumentIngestionPipeline _ingestionPipeline;

    /// <summary>
    /// Entity Framework database context for document persistence.
    /// </summary>
    private readonly GedDbContext _db;

    /// <summary>
    /// Base directory path for file storage.
    /// </summary>
    private readonly string _basePath;

    /// <summary>
    /// Initializes a new instance of <see cref="DocumentService"/>.
    /// </summary>
    /// <param name="logger">Logger for service events.</param>
    /// <param name="storageService">Service for file storage operations.</param>
    /// <param name="textExtractionService">Service for extracting text from documents.</param>
    /// <param name="db">Entity Framework database context.</param>
    /// <param name="ingestionPipeline">Pipeline for document enrichment steps.</param>
    /// <param name="dateExtractor">Optional LLM-based date extractor.</param>
    /// <param name="configuration">Application configuration.</param>
    public DocumentService(
        ILogger<DocumentService> logger,
        IStorageService storageService,
        ITextExtractionService textExtractionService,
        GedDbContext db,
        DocumentIngestionPipeline ingestionPipeline,
        DocumentDateExtractor? dateExtractor = null,
        IConfiguration? configuration = null)
    {
        _logger = logger;
        _storageService = storageService;
        _textExtractionService = textExtractionService;
        _ingestionPipeline = ingestionPipeline;
        _db = db;
        _dateExtractor = dateExtractor;
        _basePath = configuration?["Document:StoragePath"] ?? "/var/lib/ged/documents";
        Directory.CreateDirectory(_basePath);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

            // Use UUID as filename to prevent conflicts and enable content-addressable storage
            var storedFileName = $"{documentId}{fileExtension}";
            var filePath = Path.Combine(_basePath, storedFileName);

            // ── 1. Save file & compute SHA-256 hash ─────────────────────────
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
            
            // Attempt date extraction if pipeline didn't provide one and extractor is available
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

            // ── 5. Build & persist EF entity ────────────────────────────────
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

            // ── 6. Queue OCR job via outbox pattern ─────────────────────────
            // Images → TesseractDirectOcrService, scanned PDFs → OcrmyPdfOcrService
            // Both are routed inside OcrWorkerService.ProcessOcrJobAsync
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

            // Single transaction: document + outbox message + optional OCR job
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<bool> MarkDocumentAsDeletedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await _db.Documents.FindAsync(new object[] { id }, cancellationToken);
            if (entity == null) return false;

            entity.Status = DocumentStatus.Deleted;
            entity.ModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Document {DocumentId} marked as deleted", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking document {DocumentId} as deleted", id);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await _db.Documents.FindAsync(new object[] { id }, cancellationToken);
            if (entity == null) return false;

            // Delete physical file from storage
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

    /// <inheritdoc />
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

    /// <summary>
    /// Maps a database entity to a domain <see cref="Document"/> model.
    /// </summary>
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

    /// <summary>
    /// Builds metadata entities from a metadata dictionary.
    /// </summary>
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

    /// <summary>
    /// Fallback description generator if pipeline doesn't provide one.
    /// Takes the first 3 meaningful lines from extracted text.
    /// </summary>
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

    /// <summary>
    /// Fallback tag generator if pipeline doesn't provide tags.
    /// Extracts tags from filename, category, and common business keywords.
    /// </summary>
    private static List<string> GenerateTags(
        string fileName, string? category, string? extractedText)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add category as tag
        if (!string.IsNullOrWhiteSpace(category)) tags.Add(category.ToLower());

        // Add filename parts as tags (only alphabetic parts, no numbers)
        foreach (var part in Regex.Split(
                     Path.GetFileNameWithoutExtension(fileName), @"[\s_\-]+")
                 .Where(p => p.Length > 3 && !p.Any(char.IsDigit) && !Regex.IsMatch(p, @"^\d+$"))
                 .Select(p => p.ToLower()))
            tags.Add(part);

        // Add file extension as tag
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLower();
        if (!string.IsNullOrWhiteSpace(ext)) tags.Add(ext);

        // Extract business keywords from text content
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

            // Extract year from text as a tag
            var yearMatch = Regex.Match(extractedText, @"\b(20\d{2})\b");
            if (yearMatch.Success) tags.Add(yearMatch.Value);
        }

        return tags.Where(t => t.Length > 2).OrderBy(t => t).Take(15).ToList();
    }
}

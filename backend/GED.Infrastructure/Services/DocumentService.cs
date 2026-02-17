using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace GED.Infrastructure.Services;

public class DocumentService : IDocumentService
{
    private readonly ILogger<DocumentService> _logger;
    private readonly IStorageService _storageService;
    private readonly ITextExtractionService _textExtractionService;
    private readonly DocumentDateExtractor? _dateExtractor;
    private readonly string _basePath;

    public DocumentService(
        ILogger<DocumentService> logger,
        IStorageService storageService,
        ITextExtractionService textExtractionService,
        DocumentDateExtractor? dateExtractor = null)
    {
        _logger = logger;
        _storageService = storageService;
        _textExtractionService = textExtractionService;
        _dateExtractor = dateExtractor;
        _basePath = "/var/lib/ged/documents";
        Directory.CreateDirectory(_basePath);
    }

    public async Task<Document?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var metadataPath = Path.Combine(_basePath, $"{id}.json");
            if (!File.Exists(metadataPath))
                return null;

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

            // ── 1. Save file & compute hash in one pass ──────────────────────
            byte[] fileBytes;
            using (var ms = new MemoryStream())
            {
                await fileStream.CopyToAsync(ms, cancellationToken);
                fileBytes = ms.ToArray();
            }
            await File.WriteAllBytesAsync(filePath, fileBytes, cancellationToken);

            var fileInfo = new FileInfo(filePath);

            // SHA-256 hash
            var fileHash = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLower();

            // ── 2. Extract text ───────────────────────────────────────────────
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

            // ── 3. Auto-generate description from extracted text ──────────────
            var description = GenerateDescription(extractedText, fileName);

            // ── 4. Auto-generate tags ─────────────────────────────────────────
            var category = metadata?.ContainsKey("category") == true
                ? metadata["category"]?.ToString()
                : null;

            var tags = GenerateTags(fileName, category, extractedText);

            // ── 5. Extract document date ──────────────────────────────────────
            DateTime? documentDate = null;
            if (_dateExtractor != null && !string.IsNullOrWhiteSpace(extractedText))
            {
                try
                {
                    _logger.LogInformation("🗓️ Attempting to extract document date from content...");
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
                    else
                    {
                        _logger.LogWarning(
                            "❌ Document date extraction confidence too low or no date found (confidence: {Confidence:F2})",
                            dateInfo?.Confidence ?? 0);
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
                    _logger.LogWarning("⚠️ DocumentDateExtractor is not available (null)");
                if (string.IsNullOrWhiteSpace(extractedText))
                    _logger.LogWarning("⚠️ No text extracted from document, cannot extract date");
            }

            // ── 6. Build document with ALL fields populated ───────────────────
            var uploadTime = DateTime.UtcNow;
            const string defaultUser = "system";

            // Build DocumentMetadata list from the metadata dict
            var documentMetadataList = BuildDocumentMetadata(documentId, metadata, uploadTime);

            var document = new Document
            {
                Id            = documentId,
                Title         = title ?? Path.GetFileNameWithoutExtension(fileName),
                Description   = description,           // ✅ was null
                FileName      = fileName,
                FilePath      = filePath,
                ContentType   = contentType,
                FileSize      = fileInfo.Length,
                FileHash      = fileHash,              // ✅ was null
                CreatedAt     = uploadTime,
                CreatedBy     = defaultUser,           // ✅ was null
                ModifiedAt    = uploadTime,            // ✅ was null
                ModifiedBy    = defaultUser,           // ✅ was null
                DocumentDate  = documentDate,
                Status        = DocumentStatus.Indexed,
                IsOcrProcessed = false,
                OcrText       = null,                  // genuinely empty until OCR runs
                ExtractedText = extractedText,
                Metadata      = metadata,
                Tags          = tags,                  // ✅ was null
                Category      = category,
                Version       = 1,
                ParentDocumentId = null,               // intentionally null (no parent)
                DocumentMetadata = documentMetadataList, // ✅ was null
            };

            // ── 7. Persist metadata JSON ──────────────────────────────────────
            var metadataPath = Path.Combine(_basePath, $"{documentId}.json");
            var json = System.Text.Json.JsonSerializer.Serialize(document);
            await File.WriteAllTextAsync(metadataPath, json, cancellationToken);

            _logger.LogInformation(
                "📄 Document created: ID={Id}, Title={Title}, Category={Category}, " +
                "UploadDate={UploadDate}, DocumentDate={DocumentDate}, " +
                "Hash={Hash}, Tags=[{Tags}], ExtractedTextLength={Length}",
                document.Id,
                document.Title,
                document.Category,
                uploadTime.ToString("yyyy-MM-dd HH:mm:ss"),
                documentDate?.ToString("yyyy-MM-dd") ?? "NOT_EXTRACTED",
                fileHash[..12] + "…",
                string.Join(", ", tags),
                document.ExtractedText?.Length ?? 0);

            return document;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading document");
            throw;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate a short description from the first meaningful sentence(s) of
    /// the extracted text, falling back to the filename.
    /// </summary>
    private static string GenerateDescription(string? extractedText, string fileName)
    {
        if (string.IsNullOrWhiteSpace(extractedText))
            return $"Document: {Path.GetFileNameWithoutExtension(fileName)}";

        // Take the first non-trivial line (skip blank / header-only lines)
        var lines = extractedText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 15)   // skip very short header lines
            .Take(3)
            .ToList();

        if (!lines.Any())
            return $"Document: {Path.GetFileNameWithoutExtension(fileName)}";

        // Combine up to ~200 characters
        var description = string.Join(" ", lines);
        if (description.Length > 200)
            description = description[..197] + "...";

        return description;
    }

    /// <summary>
    /// Auto-generate a tag list from category, filename keywords, and common
    /// content words found in the extracted text.
    /// </summary>
    private static List<string> GenerateTags(
        string fileName,
        string? category,
        string? extractedText)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Tag from category
        if (!string.IsNullOrWhiteSpace(category))
            tags.Add(category.ToLower());

        // Tags from filename (split on underscores, dashes, spaces)
        var nameParts = Regex.Split(
                Path.GetFileNameWithoutExtension(fileName), @"[\s_\-]+")
            .Where(p => p.Length > 3)
            .Select(p => p.ToLower());
        foreach (var part in nameParts)
            tags.Add(part);

        // File extension tag
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLower();
        if (!string.IsNullOrWhiteSpace(ext))
            tags.Add(ext);

        // A handful of domain keywords from the text
        if (!string.IsNullOrWhiteSpace(extractedText))
        {
            var domainKeywords = new[]
            {
                "invoice", "contract", "agreement", "report", "proposal",
                "confidential", "draft", "final", "signed", "approved",
                "budget", "payment", "license", "legal", "nda"
            };

            var lowerText = extractedText.ToLower();
            foreach (var kw in domainKeywords)
            {
                if (lowerText.Contains(kw))
                    tags.Add(kw);
            }

            // Year found in document (e.g. "2024")
            var yearMatch = Regex.Match(extractedText, @"\b(20\d{2})\b");
            if (yearMatch.Success)
                tags.Add(yearMatch.Value);
        }

        return tags
            .Where(t => t.Length > 2)
            .OrderBy(t => t)
            .Take(15)           // cap at 15 tags
            .ToList();
    }

    /// <summary>
    /// Convert the flat metadata dictionary into a typed DocumentMetadata list
    /// so that the DocumentMetadata navigation property is never null.
    /// </summary>
    private static ICollection<DocumentMetadata> BuildDocumentMetadata(
        Guid documentId,
        Dictionary<string, object>? metadata,
        DateTime createdAt)
    {
        var list = new List<DocumentMetadata>();

        if (metadata == null)
            return list;

        foreach (var kvp in metadata)
        {
            if (kvp.Value == null) continue;

            var type = kvp.Value switch
            {
                bool   => MetadataType.Boolean,
                int or long or float or double => MetadataType.Number,
                DateTime => MetadataType.Date,
                _ => MetadataType.String
            };

            list.Add(new DocumentMetadata
            {
                Id         = Guid.NewGuid(),
                DocumentId = documentId,
                Key        = kvp.Key,
                Value      = kvp.Value.ToString(),
                Type       = type,
                CreatedAt  = createdAt
            });
        }

        return list;
    }

    // ── Standard CRUD (unchanged) ─────────────────────────────────────────────

    public async Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var document = await GetDocumentByIdAsync(id, cancellationToken);
            if (document == null) return false;

            if (File.Exists(document.FilePath))
                File.Delete(document.FilePath);

            var metadataPath = Path.Combine(_basePath, $"{id}.json");
            if (File.Exists(metadataPath))
                File.Delete(metadataPath);

            _logger.LogInformation("Document {DocumentId} deleted successfully", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document {DocumentId}", id);
            return false;
        }
    }

    public async Task<Document> UpdateDocumentAsync(
        Guid id, Document document, CancellationToken cancellationToken = default)
    {
        try
        {
            document.ModifiedAt = DateTime.UtcNow;
            document.ModifiedBy ??= "system";

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

    public async Task<Stream> GetDocumentContentAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var document = await GetDocumentByIdAsync(id, cancellationToken)
            ?? throw new FileNotFoundException($"Document {id} not found");

        if (!File.Exists(document.FilePath))
            throw new FileNotFoundException($"File not found: {document.FilePath}");

        return File.OpenRead(document.FilePath);
    }

    public async Task<List<Document>> GetDocumentsByIdsAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var documents = new List<Document>();
        foreach (var id in ids)
        {
            var doc = await GetDocumentByIdAsync(id, cancellationToken);
            if (doc != null) documents.Add(doc);
        }
        return documents;
    }
}
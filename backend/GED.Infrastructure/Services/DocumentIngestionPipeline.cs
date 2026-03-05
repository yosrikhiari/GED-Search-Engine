using GED.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace GED.Infrastructure.Services;

/// <summary>
/// Document Ingestion Pipeline.
///
/// Problem solved: DocumentService.UploadDocumentAsync was ~120 lines doing 5 jobs:
///   save file → extract text → generate description → extract date → persist.
/// This made each step untestable in isolation and errors hard to diagnose.
///
/// Solution (ByteByteGo: SOLID — Single Responsibility Principle):
///   Each ingestion step is an explicit, named method.
///   DocumentService now only handles persistence; this class handles enrichment.
///   Any step failure is logged and gracefully skipped (no silent data loss).
///
/// Steps (all optional, all fault-tolerant):
///   1. Text extraction (Tika / built-in fallback)
///   2. Description generation (keyword-based, no LLM needed)
///   3. Tag generation (from filename, category, content keywords)
///   4. Document date extraction (LLM-powered, skipped if LLM unavailable)
/// </summary>
public class DocumentIngestionPipeline
{
    private readonly ITextExtractionService              _textExtractor;
    private readonly DocumentDateExtractor?              _dateExtractor;
    private readonly ILogger<DocumentIngestionPipeline>  _logger;

    public DocumentIngestionPipeline(
        ITextExtractionService textExtractor,
        ILogger<DocumentIngestionPipeline> logger,
        DocumentDateExtractor? dateExtractor = null)   // optional — null if LLM disabled
    {
        _textExtractor = textExtractor;
        _dateExtractor = dateExtractor;
        _logger        = logger;
    }

    /// <summary>Result of a full ingestion pipeline run.</summary>
    public record IngestionResult(
        string?                    ExtractedText,
        string?                    Description,
        DateTime?                  DocumentDate,
        List<string>               Tags,
        Dictionary<string, object> Metadata
    );

    /// <summary>
    /// Run all ingestion steps. Failures in individual steps are caught and logged;
    /// the pipeline always returns a result (possibly with null fields).
    /// </summary>
    public async Task<IngestionResult> RunAsync(
        byte[]            fileBytes,
        string            fileName,
        string            contentType,
        string?           category,
        CancellationToken ct = default)
    {
        var metadata = new Dictionary<string, object>();

        // Step 1: Extract text
        var extractedText = await ExtractTextSafeAsync(fileBytes, contentType, ct);

        // Step 2: Generate description (synchronous, no LLM)
        var description = GenerateDescription(extractedText, fileName);

        // Step 3: Generate tags (synchronous, no LLM)
        var tags = GenerateTags(fileName, category, extractedText);

        // Step 4: Extract document date (LLM, optional)
        var documentDate = await ExtractDateSafeAsync(
            extractedText, fileName, category ?? "Other", metadata, ct);

        _logger.LogInformation(
            "✅ Ingestion complete for '{FileName}' — text={TextLen}chars, tags={TagCount}, date={Date}",
            fileName,
            extractedText?.Length ?? 0,
            tags.Count,
            documentDate?.ToString("yyyy-MM-dd") ?? "none");

        return new IngestionResult(extractedText, description, documentDate, tags, metadata);
    }

    // ─── Step 1: Text Extraction ──────────────────────────────────────────────
    private async Task<string?> ExtractTextSafeAsync(
        byte[] fileBytes, string contentType, CancellationToken ct)
    {
        try
        {
            using var stream = new MemoryStream(fileBytes);
            var text = await _textExtractor.ExtractTextAsync(stream, contentType, ct);
            _logger.LogDebug("Text extraction: {Chars} chars from {Type}", text?.Length ?? 0, contentType);
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Text extraction failed for {Type} — continuing without text", contentType);
            return null;
        }
    }

    // ─── Step 2: Description ─────────────────────────────────────────────────
    private static string GenerateDescription(string? extractedText, string fileName)
    {
        if (string.IsNullOrWhiteSpace(extractedText))
            return $"Document: {Path.GetFileNameWithoutExtension(fileName)}";

        // Take first 3 meaningful lines as the description
        var lines = extractedText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 15 && !Regex.IsMatch(l, @"^[\W\d]+$"))
            .Take(3)
            .ToList();

        if (!lines.Any())
            return $"Document: {Path.GetFileNameWithoutExtension(fileName)}";

        var desc = string.Join(" ", lines);
        return desc.Length > 200 ? desc[..197] + "…" : desc;
    }

    // ─── Step 3: Tag Generation ───────────────────────────────────────────────
    private static List<string> GenerateTags(string fileName, string? category, string? text)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Tag: document category
        if (!string.IsNullOrWhiteSpace(category))
            tags.Add(category.ToLower());

        // Tags: filename parts (split on spaces, dashes, underscores)
        var nameParts = Regex.Split(
            Path.GetFileNameWithoutExtension(fileName), @"[\s_\-\.]+");
        foreach (var part in nameParts.Where(p => p.Length > 3))
            tags.Add(part.ToLower());

        // Tag: file extension
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLower();
        if (!string.IsNullOrWhiteSpace(ext)) tags.Add(ext);

        // Tags: common business keywords found in content
        if (!string.IsNullOrWhiteSpace(text))
        {
            var businessKeywords = new[]
            {
                "invoice", "contract", "report", "agreement", "receipt",
                "payment", "budget", "proposal", "audit", "compliance",
                "facture", "contrat", "rapport", "devis", "bon de commande"
            };
            var textLower = text.ToLower();
            foreach (var kw in businessKeywords)
                if (textLower.Contains(kw))
                    tags.Add(kw);
        }

        return tags
            .Where(t => t.Length > 2)
            .OrderBy(t => t)
            .Take(15)
            .ToList();
    }

    // ─── Step 4: Date Extraction (LLM, optional) ─────────────────────────────
    private async Task<DateTime?> ExtractDateSafeAsync(
        string?                    extractedText,
        string                     fileName,
        string                     category,
        Dictionary<string, object> metadata,
        CancellationToken          ct)
    {
        if (_dateExtractor == null || string.IsNullOrWhiteSpace(extractedText))
            return null;

        try
        {
            var info = await _dateExtractor.ExtractDocumentDateAsync(
                extractedText, fileName, category, ct);

            if (info?.DocumentDate == null || info.Confidence < 0.5f)
            {
                _logger.LogDebug(
                    "Date extraction: no confident date found (confidence={Conf:F2})",
                    info?.Confidence ?? 0f);
                return null;
            }

            var date = DateTime.SpecifyKind(info.DocumentDate.Value, DateTimeKind.Utc);

            // Store date metadata for audit trail
            metadata["extracted_date"]     = date.ToString("yyyy-MM-dd");
            metadata["date_confidence"]    = info.Confidence;
            metadata["date_type"]          = info.DateType ?? "unknown";
            metadata["date_extracted_by"]  = "llm";

            _logger.LogInformation(
                "📅 Date extracted: {Date} (type={Type}, confidence={Conf:F2})",
                date.ToString("yyyy-MM-dd"), info.DateType, info.Confidence);

            return date;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Date extraction failed — continuing without date");
            return null;
        }
    }
}
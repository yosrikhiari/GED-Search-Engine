using GED.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace GED.Infrastructure.Services;

/// <summary>
/// Document ingestion pipeline that orchestrates all enrichment steps.
/// 
/// <para>
/// Problem solved: <c>DocumentService.UploadDocumentAsync</c> was ~120 lines doing 5 jobs:
/// save file → extract text → generate description → extract date → persist.
/// This made each step untestable in isolation and errors hard to diagnose.
/// </para>
/// 
/// <para>
/// Solution (ByteByteGo: SOLID — Single Responsibility Principle):
/// Each ingestion step is an explicit, named method.
/// <c>DocumentService</c> now only handles persistence; this class handles enrichment.
/// Any step failure is logged and gracefully skipped (no silent data loss).
/// </para>
/// 
/// Pipeline steps (all optional, all fault-tolerant):
///
/// <para>
/// This pipeline runs synchronously during document upload.
/// It extracts visible text from native-format documents (PDF with text layer,
/// DOCX, XLSX, etc.). For scanned/image documents, OCR is handled later by
/// <see cref="OcrWorkerService"/> asynchronously via RabbitMQ.
/// </para>
/// <list type="number">
///   <item>
///     <term>Text extraction</term>
///     <description>
///       Uses Apache Tika or built-in fallback to extract text from uploaded documents.
///     </description>
///   </item>
///   <item>
///     <term>Description generation</term>
///     <description>
///       Keyword-based description generation (no LLM needed, synchronous).
///     </description>
///   </item>
///   <item>
///     <term>Tag generation</term>
///     <description>
///       Tags from filename, category, and content keywords (synchronous, no LLM).
///     </description>
///   </item>
/// </list>
/// 
/// <para>
/// NOTE: LLM date extraction has been intentionally removed from this pipeline.
/// It was blocking the upload response for 5–15s per document.
/// Date extraction now runs asynchronously inside <see cref="OcrWorkerService.EnrichAndSaveAsync"/>,
/// after the document is already indexed and visible in search.
/// </para>
/// </summary>
public class DocumentIngestionPipeline
{
    private readonly ITextExtractionService              _textExtractor;
    private readonly ILogger<DocumentIngestionPipeline>  _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="DocumentIngestionPipeline"/>.
    /// </summary>
    /// <param name="textExtractor">Service for extracting text from documents.</param>
    /// <param name="logger">Logger for pipeline events.</param>
    /// <param name="dateExtractor">
    ///   (Deprecated) Kept for DI compatibility. Date extraction is now handled
    ///   asynchronously by <see cref="OcrWorkerService"/>.
    /// </param>
    public DocumentIngestionPipeline(
        ITextExtractionService textExtractor,
        ILogger<DocumentIngestionPipeline> logger,
        DocumentDateExtractor? dateExtractor = null)
    {
        _textExtractor = textExtractor;
        _logger        = logger;
    }

    /// <summary>
    /// Represents the result of a full ingestion pipeline run.
    /// </summary>
    /// <param name="ExtractedText">Extracted text from the document, or null if extraction failed.</param>
    /// <param name="Description">Generated description, or null if generation failed.</param>
    /// <param name="DocumentDate">
    ///   Always null here — set later by <see cref="OcrWorkerService"/> asynchronously.
    /// </param>
    /// <param name="Tags">Always null here — tags generated later by OcrWorkerService after OCR completes.</param>
    /// <param name="Metadata">Additional metadata extracted during processing.</param>
    public record IngestionResult(
        string?                    ExtractedText,
        string?                    Description,
        DateTime?                  DocumentDate,
        List<string>?              Tags,
        Dictionary<string, object> Metadata
    );

    /// <summary>
    /// Runs all ingestion steps for a document.
    /// </summary>
    /// <param name="fileBytes">Raw file bytes.</param>
    /// <param name="fileName">Original filename.</param>
    /// <param name="contentType">MIME type of the file.</param>
    /// <param name="category">Document category (optional).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="IngestionResult"/> with all extracted/enriched data.</returns>
    /// <remarks>
    /// Failures in individual steps are caught and logged; the pipeline always
    /// returns a result (possibly with null fields).
    /// 
    /// <c>DocumentDate</c> is always null here — it is extracted asynchronously
    /// by <c>OcrWorkerService</c> after OCR/LLM enrichment, so the upload
    /// response is fast.
    /// 
    /// Tags are NOT generated here — they are generated later by OcrWorkerService
    /// after OCR completes, either via LLM enrichment or keyword fallback.
    /// </remarks>
    public async Task<IngestionResult> RunAsync(
        byte[]            fileBytes,
        string            fileName,
        string            contentType,
        string?           category,
        CancellationToken ct = default)
    {
        var metadata = new Dictionary<string, object>();

        // Step 1: Extract text (fast — Tika or built-in parser)
        var extractedText = await ExtractTextSafeAsync(fileBytes, contentType, ct);

        // Step 2: Generate description (synchronous, no LLM)
        var description = GenerateDescription(extractedText, fileName);

        // Step 3: Tags are NOT generated here anymore
        // Tags are generated later by OcrWorkerService after OCR completes

        // Step 4 (REMOVED): LLM date extraction was here.
        // It blocked the upload for 5–15s. Moved to OcrWorkerService.EnrichAndSaveAsync.
        DateTime? documentDate = null;

        _logger.LogInformation(
            "✅ Ingestion complete for '{FileName}' — text={TextLen}chars (tags deferred to OCR worker)",
            fileName,
            extractedText?.Length ?? 0);

        return new IngestionResult(extractedText, description, documentDate, null, metadata);
    }

    /// <summary>
    /// Safely extracts text from a document, handling errors gracefully.
    /// </summary>
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

    /// <summary>
    /// Generates a description from extracted text by taking the first few meaningful lines.
    /// Falls back to "Document: {filename}" if no meaningful text is found.
    /// </summary>
    private static string GenerateDescription(string? extractedText, string fileName)
    {
        if (string.IsNullOrWhiteSpace(extractedText))
            return $"Document: {Path.GetFileNameWithoutExtension(fileName)}";

        // Take first 3 meaningful lines as the description
        // Filter out lines that are too short or contain only special characters
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

    /// <summary>
    /// Generates tags from filename, category, and content keywords.
    /// </summary>
    /// <param name="fileName">Original filename to extract name parts from.</param>
    /// <param name="category">Document category.</param>
    /// <param name="text">Extracted text to scan for business keywords.</param>
    /// <returns>List of lowercase tags, ordered and limited to 15.</returns>
    private static List<string> GenerateTags(string fileName, string? category, string? text)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Tag: document category
        if (!string.IsNullOrWhiteSpace(category))
            tags.Add(category.ToLower());

        // Tags: filename parts (only alphabetic, no numbers or pure numeric strings)
        var nameParts = Regex.Split(
            Path.GetFileNameWithoutExtension(fileName), @"[\s_\-\.]+");
        foreach (var part in nameParts.Where(p => p.Length > 3 && !p.Any(char.IsDigit) && !Regex.IsMatch(p, @"^\d+$")))
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
}

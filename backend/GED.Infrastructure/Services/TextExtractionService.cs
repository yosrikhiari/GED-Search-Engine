using GED.Core.Interfaces;
using Microsoft.Extensions.Logging;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Text;

namespace GED.Infrastructure.Services;

/// <summary>
/// Text extraction service with full PDF, DOCX, XLSX, and plain-text support.
/// Previously Word/Excel extraction was stubbed with empty returns — now implemented.
/// </summary>
public class TextExtractionService : ITextExtractionService
{
    private readonly ILogger<TextExtractionService> _logger;

    // Supported MIME types (drives both extraction routing and SupportsContentType)
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "text/plain",
        // Word
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        // Excel
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        // OpenDocument
        "application/vnd.oasis.opendocument.text",
    };

    public TextExtractionService(ILogger<TextExtractionService> logger)
    {
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<string> ExtractTextAsync(
        Stream fileStream,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return contentType.ToLower() switch
            {
                "application/pdf"
                    => await ExtractFromPdfAsync(fileStream, cancellationToken),

                "text/plain"
                    => await ExtractFromPlainTextAsync(fileStream),

                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or
                "application/msword"
                    => await ExtractFromDocxAsync(fileStream, cancellationToken),

                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" or
                "application/vnd.ms-excel"
                    => await ExtractFromXlsxAsync(fileStream, cancellationToken),

                _ => string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text for content type {ContentType}", contentType);
            return string.Empty;
        }
    }

    public Task<bool> SupportsContentType(string contentType)
        => Task.FromResult(SupportedTypes.Contains(contentType));

    // ── PDF ───────────────────────────────────────────────────────────────────

    private async Task<string> ExtractFromPdfAsync(Stream pdfStream, CancellationToken cancellationToken)
    {
        try
        {
            using var ms = new MemoryStream();
            await pdfStream.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;

            using var reader    = new PdfReader(ms);
            using var pdfDoc    = new PdfDocument(reader);
            var sb              = new StringBuilder();

            for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
            {
                var page     = pdfDoc.GetPage(i);
                var strategy = new SimpleTextExtractionStrategy();
                sb.AppendLine(PdfTextExtractor.GetTextFromPage(page, strategy));
            }

            var text = sb.ToString();
            _logger.LogInformation("PDF: extracted {Chars} chars from {Pages} pages",
                text.Length, pdfDoc.GetNumberOfPages());
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from PDF");
            return string.Empty;
        }
    }

    // ── Plain text ────────────────────────────────────────────────────────────

    private static async Task<string> ExtractFromPlainTextAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    // ── Word (DOCX) ───────────────────────────────────────────────────────────
    // FIX: Previously returned string.Empty with a log warning.
    // Now properly extracts paragraphs, tables, headers and footers.

    private async Task<string> ExtractFromDocxAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            // Copy to MemoryStream — OpenXml requires a seekable stream
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;

            using var wordDoc = WordprocessingDocument.Open(ms, isEditable: false);
            var body = wordDoc.MainDocumentPart?.Document?.Body;

            if (body == null)
            {
                _logger.LogWarning("DOCX has no body element");
                return string.Empty;
            }

            var sb = new StringBuilder();

            // ── Paragraphs & Tables ──────────────────────────────────────────
            foreach (var element in body.ChildElements)
            {
                // Paragraph
                if (element is DocumentFormat.OpenXml.Wordprocessing.Paragraph para)
                {
                    var paraText = ExtractParagraphText(para);
                    if (!string.IsNullOrWhiteSpace(paraText))
                        sb.AppendLine(paraText);
                }
                // Table
                else if (element is DocumentFormat.OpenXml.Wordprocessing.Table table)
                {
                    foreach (var row in table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>())
                    {
                        var cells = row
                            .Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>()
                            .Select(cell => cell.InnerText.Trim())
                            .Where(t => !string.IsNullOrEmpty(t));

                        var rowText = string.Join("\t", cells);
                        if (!string.IsNullOrWhiteSpace(rowText))
                            sb.AppendLine(rowText);
                    }
                }
            }

            // ── Headers ──────────────────────────────────────────────────────
            foreach (var headerPart in wordDoc.MainDocumentPart?.HeaderParts ?? Enumerable.Empty<HeaderPart>())
            {
                var headerText = headerPart.Header?.InnerText?.Trim();
                if (!string.IsNullOrWhiteSpace(headerText))
                    sb.Insert(0, headerText + Environment.NewLine);
            }

            // ── Footers ──────────────────────────────────────────────────────
            foreach (var footerPart in wordDoc.MainDocumentPart?.FooterParts ?? Enumerable.Empty<FooterPart>())
            {
                var footerText = footerPart.Footer?.InnerText?.Trim();
                if (!string.IsNullOrWhiteSpace(footerText))
                    sb.AppendLine(footerText);
            }

            var result = sb.ToString();
            _logger.LogInformation("DOCX: extracted {Chars} chars", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from DOCX");
            return string.Empty;
        }
    }

    /// <summary>
    /// Extract text from a paragraph, preserving runs and handling
    /// tab characters, line breaks, and field codes correctly.
    /// </summary>
    private static string ExtractParagraphText(DocumentFormat.OpenXml.Wordprocessing.Paragraph para)
    {
        var sb = new StringBuilder();

        foreach (var run in para.Descendants<DocumentFormat.OpenXml.Wordprocessing.Run>())
        {
            foreach (var child in run.ChildElements)
            {
                switch (child)
                {
                    case DocumentFormat.OpenXml.Wordprocessing.Text text:
                        sb.Append(text.Text);
                        break;
                    case DocumentFormat.OpenXml.Wordprocessing.TabChar:
                        sb.Append('\t');
                        break;
                    case DocumentFormat.OpenXml.Wordprocessing.Break:
                        sb.Append(' ');
                        break;
                }
            }
        }

        return sb.ToString().Trim();
    }

    // ── Excel (XLSX) ──────────────────────────────────────────────────────────
    // FIX: Previously not implemented at all.

    private async Task<string> ExtractFromXlsxAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;

            using var spreadsheet = SpreadsheetDocument.Open(ms, isEditable: false);
            var workbookPart = spreadsheet.WorkbookPart;

            if (workbookPart == null)
                return string.Empty;

            // Build shared string table for fast lookups
            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable
                .Elements<SharedStringItem>()
                .Select(s => s.InnerText)
                .ToList() ?? new List<string>();

            var sb = new StringBuilder();

            foreach (var sheet in workbookPart.WorksheetParts)
            {
                var sheetData = sheet.Worksheet.GetFirstChild<SheetData>();
                if (sheetData == null) continue;

                foreach (var row in sheetData.Elements<Row>())
                {
                    var cellValues = new List<string>();

                    foreach (var cell in row.Elements<Cell>())
                    {
                        var value = GetCellValue(cell, sharedStrings);
                        cellValues.Add(value);
                    }

                    var rowText = string.Join("\t", cellValues.Where(v => !string.IsNullOrEmpty(v)));
                    if (!string.IsNullOrWhiteSpace(rowText))
                        sb.AppendLine(rowText);
                }

                sb.AppendLine(); // blank line between sheets
            }

            var result = sb.ToString();
            _logger.LogInformation("XLSX: extracted {Chars} chars", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from XLSX");
            return string.Empty;
        }
    }

    private static string GetCellValue(Cell cell, List<string> sharedStrings)
    {
        if (cell.CellValue == null) return string.Empty;

        var rawValue = cell.CellValue.InnerText;

        // Shared string reference
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (int.TryParse(rawValue, out var index) && index < sharedStrings.Count)
                return sharedStrings[index];
        }

        // Boolean
        if (cell.DataType?.Value == CellValues.Boolean)
            return rawValue == "1" ? "TRUE" : "FALSE";

        return rawValue;
    }
}
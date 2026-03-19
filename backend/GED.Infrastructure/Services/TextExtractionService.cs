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
/// Text extraction service using built-in .NET libraries (iText, OpenXML).
///
/// <para>
/// This service serves as the fallback for <see cref="TikaTextExtractionService"/>.
/// It is used when Tika Server is unavailable, disabled, or returns an error.
/// Both services implement <see cref="ITextExtractionService"/>, allowing the
/// pipeline to continue with any available extraction method.
/// </para>
/// 
/// <para>
/// This service provides full text extraction capabilities:
/// <list type="bullet">
///   <item>
///     <term>PDF</term>
///     <description>
///       Uses iText to extract text from PDF documents, including those with
///       native text layers. Processes all pages sequentially.
///     </description>
///   </item>
///   <item>
///     <term>DOCX (Word)</term>
///     <description>
///       Extracts text from Word documents including paragraphs, tables,
///       headers, and footers.
///     </description>
///   </item>
///   <item>
///     <term>XLSX (Excel)</term>
///     <description>
///       Extracts text content from Excel spreadsheets, including all sheets.
///       Uses shared string table for efficient string lookup.
///     </description>
///   </item>
///   <item>
///     <term>Plain text</term>
///     <description>
///       Reads text files directly with UTF-8 encoding.
///     </description>
///   </item>
/// </list>
/// </para>
/// 
/// <para>
/// All extraction methods are fault-tolerant and return empty strings on failure,
/// allowing the calling code to continue without interruption.
/// </para>
/// </summary>
public class TextExtractionService : ITextExtractionService
{
    private readonly ILogger<TextExtractionService> _logger;

    /// <summary>
    /// Set of supported MIME types for text extraction.
    /// Drives both extraction routing and <see cref="SupportsContentType"/> checks.
    /// </summary>
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "text/plain",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    /// <summary>
    /// Initializes a new instance of <see cref="TextExtractionService"/>.
    /// </summary>
    /// <param name="logger">Logger for extraction events.</param>
    public TextExtractionService(ILogger<TextExtractionService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ExtractTextAsync(
        Stream fileStream,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return contentType.ToLower() switch
            {
                // PDF extraction
                "application/pdf"
                    => await ExtractFromPdfAsync(fileStream, cancellationToken),

                // Plain text
                "text/plain"
                    => await ExtractFromPlainTextAsync(fileStream),

                // Word documents (both legacy .doc and modern .docx)
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or
                "application/msword"
                    => await ExtractFromDocxAsync(fileStream, cancellationToken),

                // Excel spreadsheets (both legacy .xls and modern .xlsx)
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" or
                "application/vnd.ms-excel"
                    => await ExtractFromXlsxAsync(fileStream, cancellationToken),
    
                // Unsupported format
                _ => string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text for content type {ContentType}", contentType);
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public Task<bool> SupportsContentType(string contentType)
        => Task.FromResult(SupportedTypes.Contains(contentType));

    // ── PDF extraction ────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts text from a PDF document using iText.
    /// </summary>
    /// <param name="pdfStream">PDF file stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted text from all pages.</returns>
    private async Task<string> ExtractFromPdfAsync(Stream pdfStream, CancellationToken cancellationToken)
    {
        try
        {
            // Copy to MemoryStream because iText requires seekable stream
            using var ms = new MemoryStream();
            await pdfStream.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;

            using var reader    = new PdfReader(ms);
            using var pdfDoc    = new PdfDocument(reader);
            var sb              = new StringBuilder();

            // Extract text from each page
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

    // ── Plain text extraction ────────────────────────────────────────────────

    /// <summary>
    /// Reads plain text from a stream.
    /// </summary>
    /// <param name="stream">Text file stream.</param>
    /// <returns>File contents as string.</returns>
    private static async Task<string> ExtractFromPlainTextAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    // ── DOCX (Word) extraction ────────────────────────────────────────────────

    /// <summary>
    /// Extracts text from a Word document (DOCX format).
    /// </summary>
    /// <param name="stream">DOCX file stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted text including paragraphs, tables, headers, and footers.</returns>
    /// <remarks>
    /// Extracts from:
    /// <list type="bullet">
    ///   <item>Main document body (paragraphs and tables)</item>
    ///   <item>Headers</item>
    ///   <item>Footers</item>
    /// </list>
    /// Tables are formatted with tab separators for readability.
    /// </remarks>
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

            // Extract paragraphs and tables
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
                        // Extract cell text and join with tabs
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

            // Extract headers
            foreach (var headerPart in wordDoc.MainDocumentPart?.HeaderParts ?? Enumerable.Empty<HeaderPart>())
            {
                var headerText = headerPart.Header?.InnerText?.Trim();
                if (!string.IsNullOrWhiteSpace(headerText))
                    sb.Insert(0, headerText + Environment.NewLine);
            }

            // Extract footers
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
    /// Extracts text from a paragraph, preserving runs and handling
    /// tab characters, line breaks, and field codes correctly.
    /// </summary>
    /// <param name="para">Word paragraph element.</param>
    /// <returns>Extracted paragraph text.</returns>
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

    // ── XLSX (Excel) extraction ────────────────────────────────────────────────

    /// <summary>
    /// Extracts text from an Excel spreadsheet (XLSX format).
    /// </summary>
    /// <param name="stream">XLSX file stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted text with rows separated by newlines, sheets by blank lines.</returns>
    /// <remarks>
    /// Uses the shared string table for efficient string lookup.
    /// Each sheet is separated by a blank line in the output.
    /// </remarks>
    private async Task<string> ExtractFromXlsxAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            // Copy to MemoryStream (OpenXml requires seekable stream)
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

            // Process each worksheet
            foreach (var sheet in workbookPart.WorksheetParts)
            {
                var sheetData = sheet.Worksheet.GetFirstChild<SheetData>();
                if (sheetData == null) continue;

                // Extract each row
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

                sb.AppendLine(); // Blank line between sheets
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

    /// <summary>
    /// Gets the display value of an Excel cell, handling shared strings and data types.
    /// </summary>
    /// <param name="cell">Excel cell element.</param>
    /// <param name="sharedStrings">Shared string table for the workbook.</param>
    /// <returns>Cell value as string.</returns>
    private static string GetCellValue(Cell cell, List<string> sharedStrings)
    {
        if (cell.CellValue == null) return string.Empty;

        var rawValue = cell.CellValue.InnerText;

        // Shared string reference (most common for text cells)
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (int.TryParse(rawValue, out var index) && index < sharedStrings.Count)
                return sharedStrings[index];
        }

        // Boolean values
        if (cell.DataType?.Value == CellValues.Boolean)
            return rawValue == "1" ? "TRUE" : "FALSE";

        // Numeric and other values — return raw text
        return rawValue;
    }
}

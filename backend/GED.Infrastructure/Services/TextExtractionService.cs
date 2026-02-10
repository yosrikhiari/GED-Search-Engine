using GED.Core.Interfaces;
using Microsoft.Extensions.Logging;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace GED.Infrastructure.Services;

public class TextExtractionService : ITextExtractionService
{
    private readonly ILogger<TextExtractionService> _logger;

    public TextExtractionService(ILogger<TextExtractionService> logger)
    {
        _logger = logger;
    }

    public async Task<string> ExtractTextAsync(Stream fileStream, string contentType, CancellationToken cancellationToken = default)
    {
        try
        {
            if (contentType == "application/pdf")
            {
                return await ExtractFromPdfAsync(fileStream, cancellationToken);
            }
            else if (contentType == "text/plain")
            {
                using var reader = new StreamReader(fileStream);
                return await reader.ReadToEndAsync();
            }
            else if (contentType.Contains("word") || contentType.Contains("document"))
            {
                // For Word documents, would need additional library like DocumentFormat.OpenXml
                _logger.LogWarning("Word document text extraction not fully implemented");
                return string.Empty;
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from document");
            return string.Empty;
        }
    }

    private async Task<string> ExtractFromPdfAsync(Stream pdfStream, CancellationToken cancellationToken)
    {
        try
        {
            using var memoryStream = new MemoryStream();
            await pdfStream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            using var pdfReader = new PdfReader(memoryStream);
            using var pdfDocument = new PdfDocument(pdfReader);

            var text = new System.Text.StringBuilder();

            for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
            {
                var page = pdfDocument.GetPage(i);
                var strategy = new SimpleTextExtractionStrategy();
                var pageText = PdfTextExtractor.GetTextFromPage(page, strategy);
                text.AppendLine(pageText);
            }

            return text.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from PDF");
            return string.Empty;
        }
    }

    public Task<bool> SupportsContentType(string contentType)
    {
        var supportedTypes = new[]
        {
            "application/pdf",
            "text/plain"
        };

        return Task.FromResult(supportedTypes.Contains(contentType));
    }
}
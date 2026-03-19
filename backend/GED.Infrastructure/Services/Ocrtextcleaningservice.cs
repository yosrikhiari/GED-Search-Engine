using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GED.Infrastructure.Services;

/// <summary>
/// Uses Ollama (local LLM) to clean up raw OCR text that contains common
/// OCR artifacts such as broken words, garbled characters, misread letters (0/O, 1/l/I),
/// random line breaks mid-word, stray symbols, etc.
/// 
/// <para>
/// This runs AFTER Tesseract extraction and BEFORE date/metadata extraction so that
/// downstream services receive clean, readable text.
/// </para>
/// 
/// <para>
/// Text cleaning rules applied:
/// <list type="bullet">
///   <item>Fix broken words split across lines</item>
///   <item>Correct misread characters (0→O, 1→l/I, rn→m)</item>
///   <item>Remove stray symbols between words</item>
///   <item>Fix extra spaces within words</item>
///   <item>Add missing spaces between words</item>
///   <item>Correct garbled numbers in dates</item>
/// </list>
/// </para>
/// 
/// <para>
/// Important constraints:
/// <list type="bullet">
///   <item>Preserves ALL original content — no summarization</item>
///   <item>Preserves original structure (paragraphs, line breaks)</item>
///   <item>Returns original text on any failure</item>
/// </list>
/// </para>
/// </summary>
public class OcrTextCleaningService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OcrTextCleaningService> _logger;

    /// <summary>
    /// Ollama API endpoint for text cleaning requests.
    /// </summary>
    private readonly string _endpoint;

    /// <summary>
    /// Ollama model name for text cleaning.
    /// </summary>
    private readonly string _model;

    /// <summary>
    /// Whether the cleaning service is enabled.
    /// </summary>
    private readonly bool _enabled;

    /// <summary>
    /// Maximum characters sent to LLM in one call (keeps prompt under context window).
    /// </summary>
    private const int MaxCharsPerChunk = 4000;

    /// <summary>
    /// Minimum text length worth sending to LLM (very short text = not worth the round-trip).
    /// </summary>
    private const int MinCharsToClean = 20;

    /// <summary>
    /// Timeout for each LLM cleaning request in seconds.
    /// </summary>
    private const int ChunkTimeoutSeconds = 60;

    /// <summary>
    /// Initializes a new instance of <see cref="OcrTextCleaningService"/>.
    /// </summary>
    /// <param name="httpClient">HTTP client for Ollama API calls.</param>
    /// <param name="logger">Logger for cleaning events.</param>
    /// <param name="configuration">Application configuration with NLP:* settings.</param>
    public OcrTextCleaningService(
        HttpClient httpClient,
        ILogger<OcrTextCleaningService> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger     = logger;
        _endpoint   = configuration["NLP:LlmApiEndpoint"] ?? "http://localhost:11434/api/generate";
        _model      = configuration["NLP:Model"] ?? "llama3.2";
        _enabled    = configuration.GetValue<bool>("NLP:Enabled", true);
    }

    /// <summary>
    /// Cleans OCR-extracted text using Ollama.
    /// </summary>
    /// <param name="rawOcrText">Raw text from OCR to clean.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Cleaned text, or the original text if cleaning fails/disabled.
    /// </returns>
    /// <remarks>
    /// Returns the original text unchanged if:
    /// <list type="bullet">
    ///   <item>NLP is disabled</item>
    ///   <item>Text is empty or whitespace</item>
    ///   <item>Text is shorter than MinCharsToClean</item>
    ///   <item>LLM returns empty text</item>
    ///   <item>Cleaned text is less than 30% of original (suspicious)</item>
    /// </list>
    /// </remarks>
    public async Task<string> CleanOcrTextAsync(
        string rawOcrText,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            _logger.LogDebug("OCR text cleaning disabled (NLP:Enabled=false)");
            return rawOcrText;
        }

        if (string.IsNullOrWhiteSpace(rawOcrText))
            return rawOcrText;

        if (rawOcrText.Trim().Length < MinCharsToClean)
        {
            _logger.LogDebug("OCR text too short to clean ({Len} chars), skipping", rawOcrText.Length);
            return rawOcrText;
        }

        try
        {
            _logger.LogInformation(
                "🧹 Cleaning OCR text with Ollama (model={Model}, {Chars} chars)",
                _model, rawOcrText.Length);

            string cleaned;

            if (rawOcrText.Length <= MaxCharsPerChunk)
            {
                // Clean in a single call
                cleaned = await CleanChunkAsync(rawOcrText, cancellationToken);
            }
            else
            {
                // Split into chunks, clean each, then rejoin
                cleaned = await CleanInChunksAsync(rawOcrText, cancellationToken);
            }

            // Sanity check: if the LLM returned something wildly different or empty,
            // fall back to the original text
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                _logger.LogWarning("Ollama returned empty text — keeping original OCR output");
                return rawOcrText;
            }

            // Basic sanity: cleaned text shouldn't be less than 30% of original
            // (guards against hallucination / summarisation instead of cleaning)
            var lengthRatio = (double)cleaned.Length / rawOcrText.Length;
            if (lengthRatio < 0.3)
            {
                _logger.LogWarning(
                    "Cleaned text is only {Ratio:P0} of original length — keeping original " +
                    "(original: {OrigLen} chars, cleaned: {CleanLen} chars)",
                    lengthRatio, rawOcrText.Length, cleaned.Length);
                return rawOcrText;
            }

            _logger.LogInformation(
                "✅ OCR text cleaned: {Before} → {After} chars ({Ratio:P0} ratio)",
                rawOcrText.Length, cleaned.Length, lengthRatio);

            return cleaned;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OCR text cleaning failed — returning original text");
            return rawOcrText;
        }
    }

    /// <summary>
    /// Cleans a single chunk of OCR text using Ollama.
    /// </summary>
    /// <param name="chunk">Text chunk to clean.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cleaned text, or original chunk on failure.</returns>
    private async Task<string> CleanChunkAsync(string chunk, CancellationToken cancellationToken)
    {
        var prompt = BuildCleaningPrompt(chunk);

        var requestBody = new
        {
            model       = _model,
            prompt      = prompt,
            stream      = false,
            temperature = 0.1,   // Very low temperature — deterministic correction, not creativity
            options = new
            {
                num_predict = 2048  // Allow enough tokens for the cleaned output
            }
        };

        _logger.LogDebug("Calling Ollama at {Endpoint} for OCR cleaning", _endpoint);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(ChunkTimeoutSeconds));

        var response = await _httpClient.PostAsJsonAsync(_endpoint, requestBody, cts.Token);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Ollama returned {Status} for OCR cleaning: {Body}",
                response.StatusCode, errorBody);
            return chunk; // Fall back to original chunk
        }

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken);

        var cleaned = result?.Response?.Trim() ?? string.Empty;

        // Strip any accidental markdown fences the LLM might have added
        cleaned = StripMarkdownFences(cleaned);

        _logger.LogDebug(
            "Ollama OCR cleaning: chunk {Before} chars → {After} chars",
            chunk.Length, cleaned.Length);

        return cleaned;
    }

    /// <summary>
    /// Cleans long text by splitting into paragraphs and processing each chunk.
    /// </summary>
    /// <param name="text">Full text to clean.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cleaned text.</returns>
    private async Task<string> CleanInChunksAsync(string text, CancellationToken cancellationToken)
    {
        // Split on paragraph boundaries to avoid cutting sentences mid-word
        var paragraphs = Regex.Split(text, @"\n{2,}");
        var chunks      = new List<string>();
        var currentChunk = new System.Text.StringBuilder();

        foreach (var para in paragraphs)
        {
            if (currentChunk.Length + para.Length > MaxCharsPerChunk && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString());
                currentChunk.Clear();
            }
            currentChunk.AppendLine(para);
        }

        if (currentChunk.Length > 0)
            chunks.Add(currentChunk.ToString());

        _logger.LogInformation(
            "Cleaning OCR text in {Count} chunks (total {Total} chars)",
            chunks.Count, text.Length);

        var cleanedChunks = new List<string>();

        foreach (var (chunk, idx) in chunks.Select((c, i) => (c, i)))
        {
            _logger.LogDebug("Cleaning chunk {Idx}/{Total}", idx + 1, chunks.Count);
            var cleaned = await CleanChunkAsync(chunk, cancellationToken);
            cleanedChunks.Add(cleaned);

            // Small delay between Ollama calls to avoid overwhelming it
            if (idx < chunks.Count - 1)
                await Task.Delay(100, cancellationToken);
        }

        // Rejoin with paragraph separators
        return string.Join("\n\n", cleanedChunks);
    }

    /// <summary>
    /// Builds the prompt for OCR text cleaning with explicit rules.
    /// </summary>
    private static string BuildCleaningPrompt(string rawText)
    {
        return $@"You are an OCR text correction assistant. Your job is to clean up text that was extracted from an image using OCR (Optical Character Recognition). OCR often produces errors like:

- Broken words split across lines (e.g., ""con-\ntract"" → ""contract"")
- Misread characters (e.g., ""0"" instead of ""O"", ""1"" instead of ""l"" or ""I"", ""rn"" instead of ""m"")
- Random symbols inserted between words (e.g., ""date|of"" → ""date of"")
- Extra spaces within words (e.g., ""cont ract"" → ""contract"")
- Missing spaces between words (e.g., ""thecontract"" → ""the contract"")
- Garbled numbers in dates (e.g., ""Oecember"" → ""December"", ""2O24"" → ""2024"")
- Stray punctuation or noise characters

RULES:
1. Fix OCR errors and reconstruct the original text as accurately as possible
2. Preserve ALL original content — do NOT summarize, shorten, or add new information
3. Preserve the original structure (paragraphs, line breaks, sections)
4. Preserve all dates, numbers, names, and amounts — just fix obvious OCR misreads
5. If a word or phrase is unclear and could have multiple interpretations, keep the most likely reading
6. Output ONLY the cleaned text — no explanations, no preamble, no markdown formatting

RAW OCR TEXT:
{rawText}

CLEANED TEXT:";
    }

    /// <summary>
    /// Strips markdown code fences from text.
    /// </summary>
    private static string StripMarkdownFences(string text)
    {
        // Remove ```...``` blocks if the LLM wrapped the output
        var stripped = Regex.Replace(text, @"^```[a-z]*\s*", "", RegexOptions.Multiline);
        stripped     = Regex.Replace(stripped, @"```\s*$", "", RegexOptions.Multiline);
        return stripped.Trim();
    }

    /// <summary>
    /// Ollama API response wrapper.
    /// </summary>
    private class OllamaResponse
    {
        /// <summary>
        /// The generated text response from Ollama.
        /// </summary>
        public string? Response { get; set; }
    }
}

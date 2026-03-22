using System.Text.RegularExpressions;

namespace GED.Infrastructure.Services;

/// <summary>
/// Utility class for input sanitization to prevent XSS and injection attacks.
/// </summary>
public static class InputSanitizer
{
    // HTML tags that should be stripped
    private static readonly Regex HtmlTagPattern = new(
        @"<[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Script patterns
    private static readonly Regex ScriptPattern = new(
        @"<script[^>]*>.*?</script>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    // Event handler patterns (onclick, onerror, etc.)
    private static readonly Regex EventHandlerPattern = new(
        @"\bon\w+\s*=",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Sanitizes a string input by removing HTML tags and potentially dangerous content.
    /// </summary>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Remove script tags
        var sanitized = ScriptPattern.Replace(input, string.Empty);

        // Remove event handlers
        sanitized = EventHandlerPattern.Replace(sanitized, string.Empty);

        // Remove HTML tags
        sanitized = HtmlTagPattern.Replace(sanitized, string.Empty);

        return sanitized.Trim();
    }

    /// <summary>
    /// Sanitizes a string for use in search queries.
    /// </summary>
    public static string SanitizeSearchQuery(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Remove potentially dangerous characters for OpenSearch
        var sanitized = input
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Replace("\t", " ");

        // Limit length
        if (sanitized.Length > 1000)
            sanitized = sanitized[..1000];

        return sanitized.Trim();
    }

    /// <summary>
    /// Sanitizes a filename, removing path traversal and dangerous characters.
    /// </summary>
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "unnamed_file";

        // Get just the filename (remove path)
        var name = Path.GetFileName(fileName);

        // Remove potentially dangerous characters
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
            name = name.Replace(c, '_');

        // Limit length
        if (name.Length > 200)
            name = name[..200];

        return string.IsNullOrWhiteSpace(name) ? "unnamed_file" : name;
    }

    /// <summary>
    /// Sanitizes a category name to prevent injection.
    /// </summary>
    public static string SanitizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return string.Empty;

        // Only allow alphanumeric, spaces, and common punctuation
        var sanitized = Regex.Replace(category, @"[^a-zA-Z0-9\s\-_]", string.Empty);

        return sanitized.Trim();
    }

    /// <summary>
    /// Sanitizes tags by removing dangerous content.
    /// </summary>
    public static List<string> SanitizeTags(List<string>? tags)
    {
        if (tags == null || !tags.Any())
            return new List<string>();

        return tags
            .Select(t => Sanitize(t))
            .Where(t => !string.IsNullOrWhiteSpace(t) && t.Length <= 50)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
    }
}
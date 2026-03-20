using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

public interface IOfficeOnlineService
{
    /// <summary>
    /// Generates a Microsoft Office Online (M365) preview URL for a document.
    /// </summary>
    string? GenerateViewerUrl(string documentUrl, string contentType);

    /// <summary>
    /// Generates a shareable, time-limited viewer URL with embedded auth token.
    /// </summary>
    OfficeOnlineShareResult GenerateShareableUrl(
        string documentUrl,
        string contentType,
        string title,
        TimeSpan validFor);

    /// <summary>
    /// Checks if a content type is supported by Office Online.
    /// </summary>
    bool IsSupported(string contentType);
}

public class OfficeOnlineService : IOfficeOnlineService
{
    private readonly ILogger<OfficeOnlineService> _logger;
    private readonly string? _tenantId;
    private readonly string? _clientId;
    private readonly string _baseUrl;

    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", // .docx
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",       // .xlsx
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation", // .pptx
        "application/pdf",                                                         // PDF via Office Online
    };

    private static readonly Dictionary<string, string> ContentTypeToExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/msword"] = "doc",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = "docx",
        ["application/vnd.ms-excel"] = "xls",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = "xlsx",
        ["application/vnd.ms-powerpoint"] = "ppt",
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = "pptx",
        ["application/pdf"] = "pdf",
    };

    public OfficeOnlineService(
        ILogger<OfficeOnlineService> logger,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _logger = logger;
        _tenantId = configuration["OfficeOnline:TenantId"];
        _clientId = configuration["OfficeOnline:ClientId"];
        _baseUrl = configuration["OfficeOnline:BaseUrl"] ?? "https://view.officeapps.live.com";
    }

    public bool IsSupported(string contentType)
        => SupportedTypes.Contains(contentType);

    public string? GenerateViewerUrl(string documentUrl, string contentType)
    {
        if (string.IsNullOrWhiteSpace(documentUrl))
        {
            _logger.LogWarning("Cannot generate Office Online URL: document URL is empty");
            return null;
        }

        if (!IsSupported(contentType))
        {
            _logger.LogDebug(
                "Content type '{ContentType}' is not supported by Office Online",
                contentType);
            return null;
        }

        // URL-encode the source document URL
        var encodedUrl = Uri.EscapeDataString(documentUrl);

        // Use the public Office Online viewer
        var viewerUrl = $"{_baseUrl}/op/embed.aspx?src={encodedUrl}";

        _logger.LogInformation(
            "Generated Office Online viewer URL for content type '{ContentType}'",
            contentType);

        return viewerUrl;
    }

    public OfficeOnlineShareResult GenerateShareableUrl(
        string documentUrl,
        string contentType,
        string title,
        TimeSpan validFor)
    {
        var supported = IsSupported(contentType);

        if (!supported)
        {
            return new OfficeOnlineShareResult
            {
                Success = false,
                Error = $"Content type '{contentType}' is not supported by Office Online"
            };
        }

        if (string.IsNullOrWhiteSpace(documentUrl))
        {
            return new OfficeOnlineShareResult
            {
                Success = false,
                Error = "Document URL is required"
            };
        }

        var expiresAt = DateTime.UtcNow.Add(validFor);

        // Generate a short-lived token for the share URL
        var token = GenerateShareToken(documentUrl, expiresAt);

        // Build the shareable URL with embedded auth
        var shareUrl = $"https://ged.example.com/api/office/preview" +
            $"?doc={Uri.EscapeDataString(documentUrl)}" +
            $"&type={Uri.EscapeDataString(contentType)}" +
            $"&title={Uri.EscapeDataString(title)}" +
            $"&expires={expiresAt.Ticks}" +
            $"&token={token}";

        _logger.LogInformation(
            "Generated shareable Office Online URL for '{Title}', expires at {ExpiresAt}",
            title, expiresAt);

        return new OfficeOnlineShareResult
        {
            Success = true,
            ShareUrl = shareUrl,
            ViewerUrl = GenerateViewerUrl(documentUrl, contentType),
            ExpiresAt = expiresAt,
            ContentType = contentType,
            Title = title
        };
    }

    private string GenerateShareToken(string documentUrl, DateTime expiresAt)
    {
        // Simple HMAC-based token for URL integrity
        // In production, use a proper JWT or OAuth2 token from Azure AD
        var secret = $"ged-share-{_tenantId ?? "demo"}";
        var data = $"{documentUrl}|{expiresAt.Ticks}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}

public class OfficeOnlineShareResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ShareUrl { get; set; }
    public string? ViewerUrl { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? ContentType { get; set; }
    public string? Title { get; set; }
}

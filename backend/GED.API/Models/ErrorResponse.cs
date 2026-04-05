namespace GED.API.Models;

public record ErrorResponse(string Error, string? Detail = null)
{
    public static ErrorResponse Create(string error, string? detail = null)
        => new(error, detail);
}

public record DetailedErrorResponse(
    string Error,
    string? Detail = null,
    string? Code = null,
    string? CorrelationId = null,
    string? Timestamp = null,
    string? Path = null)
{
    public static DetailedErrorResponse Create(
        string error,
        string? detail = null,
        string? code = null,
        string? correlationId = null,
        string? path = null)
        => new(
            error,
            detail,
            code,
            correlationId,
            DateTime.UtcNow.ToString("o"),
            path);
}

using System.Net;
using System.Text.Json;
using Serilog;

namespace GED.API.Middleware;

/// <summary>
/// Global exception handler middleware that catches unhandled exceptions,
/// logs them with full context, and returns a structured error response.
/// </summary>
public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items["CorrelationId"]?.ToString() 
            ?? context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N")[..12];

        // Log full exception with context
        Log.Error(exception,
            "Unhandled exception. CorrelationId={CorrelationId}, Path={Path}, Method={Method}, User={User}",
            correlationId,
            context.Request.Path,
            context.Request.Method,
            context.User.Identity?.Name ?? "Anonymous");

        // Determine status code and message based on exception type
        var (statusCode, message, details) = GetExceptionDetails(exception);

        // Build structured error response
        var errorResponse = new
        {
            error = new
            {
                code = GetErrorCode(exception),
                message = message,
                details = details,
                correlationId = correlationId,
                timestamp = DateTime.UtcNow.ToString("o"),
                path = context.Request.Path.Value
            }
        };

        // Set response
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        await context.Response.WriteAsync(json);
    }

    private static (int statusCode, string message, string? details) GetExceptionDetails(Exception ex)
    {
        return ex switch
        {
            ArgumentException argEx => (400, argEx.Message, null),
            KeyNotFoundException => (404, "Resource not found", null),
            UnauthorizedAccessException => (401, "Access denied", null),
            InvalidOperationException => (409, ex.Message, null),
            TimeoutException => (408, "The request timed out", ex.Message),
            NotImplementedException => (501, "Feature not implemented", null),
            _ => (500, "An unexpected error occurred", GetSensitiveDetails(ex))
        };
    }

    private static string GetErrorCode(Exception ex)
    {
        return ex.GetType().Name
            .Replace("Exception", "")
            .Replace("IOException", "IO_ERROR")
            .Replace("InvalidOperationException", "INVALID_OPERATION")
            .Replace("ArgumentException", "BAD_REQUEST")
            .ToUpperInvariant();
    }

    private static string? GetSensitiveDetails(Exception ex)
    {
        // Only include non-sensitive details in development
#if DEBUG
        return ex.ToString();
#else
        return null;
#endif
    }
}

/// <summary>
/// Extension method to register the global exception handler middleware.
/// </summary>
public static class GlobalExceptionHandlerMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<GlobalExceptionHandlerMiddleware>();
    }
}
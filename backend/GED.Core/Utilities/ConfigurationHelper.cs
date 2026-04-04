using System.Text.RegularExpressions;

namespace GED.Core.Utilities;

/// <summary>
/// Provides shared configuration helper methods for all services.
/// </summary>
public static class ConfigurationHelper
{
    /// <summary>
    /// Masks sensitive information in a connection string (password, user id).
    /// </summary>
    public static string MaskConnectionString(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return "(empty)";

        try
        {
            var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var maskedParts = parts.Select(part =>
            {
                var key = part.Split('=', 2)[0].Trim();
                if (key.Equals("password", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("pwd", StringComparison.OrdinalIgnoreCase))
                    return "Password=****";
                if (key.Equals("user id", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("user", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("uid", StringComparison.OrdinalIgnoreCase))
                    return "User Id=****";
                return part;
            });
            return string.Join(";", maskedParts);
        }
        catch
        {
            return "**** (masking failed)";
        }
    }

    /// <summary>
    /// Resolves environment variable placeholders in the format ${VAR_NAME} or ${VAR_NAME:-default}.
    /// </summary>
    public static string ResolveEnvironmentVariables(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return Regex.Replace(
            value,
            @"\$\{([^}:]+)(?::-([^}]*))?\}",
            match =>
            {
                var varName = match.Groups[1].Value;
                var defaultValue = match.Groups[2].Success ? match.Groups[2].Value : "";
                return Environment.GetEnvironmentVariable(varName) ?? defaultValue;
            });
    }
}

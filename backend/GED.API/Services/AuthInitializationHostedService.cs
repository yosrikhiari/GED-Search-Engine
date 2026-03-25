using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GED.Infrastructure.Services;

namespace GED.API.Services;

/// <summary>
/// Hosted service that initializes AuthService at application startup.
/// This extracts the file I/O side effects from the AuthService constructor,
/// making it testable without file system dependencies.
/// </summary>
public class AuthInitializationHostedService : IHostedService
{
    private readonly AuthService _authService;
    private readonly ILogger<AuthInitializationHostedService> _logger;

    public AuthInitializationHostedService(
        AuthService authService,
        ILogger<AuthInitializationHostedService> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting AuthService initialization...");
            await _authService.InitializeAsync();
            _logger.LogInformation("AuthService initialization completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize AuthService");
            // Don't throw - allow app to start, AuthService will fail gracefully on first use
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to clean up
        return Task.CompletedTask;
    }
}
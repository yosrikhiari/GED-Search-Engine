using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace GED.API.Services;

/// <summary>
/// Background service that establishes RabbitMQ connection asynchronously on startup.
/// This replaces the blocking Lazy&lt;IConnection&gt; that was causing startup timeouts.
/// </summary>
public class RabbitMqConnectionService : IHostedService
{
    private readonly ILogger<RabbitMqConnectionService> _logger;
    private readonly string _host;
    private readonly string _username;
    private readonly string _password;
    private IConnection? _connection;

    public IConnection? Connection => _connection;

    public RabbitMqConnectionService(
        ILogger<RabbitMqConnectionService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        
        // Get from configuration with environment variable resolution
        var hostRaw = configuration["RabbitMQ:Host"] ?? "localhost";
        var userRaw = configuration["RabbitMQ:Username"] ?? "admin";
        var passRaw = configuration["RabbitMQ:Password"] ?? "";
        
        _host = ResolveEnvVar(hostRaw);
        _username = ResolveEnvVar(userRaw);
        _password = ResolveEnvVar(passRaw);
    }

    private static string ResolveEnvVar(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        
        return System.Text.RegularExpressions.Regex.Replace(
            value,
            @"\$\{([^}:]+)(?::-([^}]*))?\}",
            m => Environment.GetEnvironmentVariable(m.Groups[1].Value) 
                 ?? (m.Groups[2].Success ? m.Groups[2].Value : ""));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Connecting to RabbitMQ at {Host}...", _host);

            var factory = new ConnectionFactory
            {
                HostName = _host,
                UserName = _username,
                Password = _password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                RequestedHeartbeat = TimeSpan.FromSeconds(60),
                RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
            };

            // Async connection - no blocking
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            
            _logger.LogInformation("✅ RabbitMQ connection established asynchronously");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to RabbitMQ on startup - will retry on first use");
            // Don't throw - allow app to start, connection will be retried on use
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_connection?.IsOpen == true)
        {
            await _connection.CloseAsync(cancellationToken);
            _logger.LogInformation("RabbitMQ connection closed");
        }
        
        _connection?.Dispose();
    }
}
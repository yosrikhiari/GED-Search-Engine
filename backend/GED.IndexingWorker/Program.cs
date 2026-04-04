using GED.Core.Interfaces;
using GED.Core.Utilities;
using GED.Infrastructure.Data;
using GED.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSearch.Client;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDirectory);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(logDirectory, "indexing-worker-.txt"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

var isDevelopment = builder.Environment.IsDevelopment();

var rawConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var connectionString = ConfigurationHelper.ResolveEnvironmentVariables(rawConnectionString ?? "");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "FATAL: ConnectionStrings__DefaultConnection is not set. " +
        "Please configure the database connection via environment variable.");
}

if (!connectionString.Contains("Pooling=", StringComparison.OrdinalIgnoreCase))
{
    var separator = connectionString.Contains(';') ? ";" : "";
    connectionString += $"{separator}Pooling=true;Min Pool Size=5;Max Pool Size=100;";
}

Log.Information("Database connection configured (password masked): {ConnStr}", ConfigurationHelper.MaskConnectionString(connectionString));

builder.Services.AddDbContext<GedDbContext>(options =>
    options.UseSqlServer(connectionString, sqlServer =>
    {
        sqlServer.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), null);
    })
);

var opensearchUrlRaw = builder.Configuration["OpenSearch:Url"] ?? "http://localhost:9200";
var opensearchUrl = ConfigurationHelper.ResolveEnvironmentVariables(opensearchUrlRaw);
var opensearchUsername = ConfigurationHelper.ResolveEnvironmentVariables(builder.Configuration["OpenSearch:Username"] ?? "");
var opensearchPassword = ConfigurationHelper.ResolveEnvironmentVariables(builder.Configuration["OpenSearch:Password"] ?? "");
var opensearchSecurityEnabledRaw = ConfigurationHelper.ResolveEnvironmentVariables(builder.Configuration["OpenSearch:SecurityEnabled"] ?? "false");
var opensearchSecurityEnabled = bool.TryParse(opensearchSecurityEnabledRaw, out var osSec) && osSec;

var connectionSettings = new ConnectionSettings(new Uri(opensearchUrl))
    .DefaultIndex("ged-documents");

if (opensearchSecurityEnabled && !string.IsNullOrEmpty(opensearchUsername))
{
    connectionSettings.BasicAuthentication(opensearchUsername, opensearchPassword);
    Log.Information("OpenSearch security enabled with user: {Username}", opensearchUsername);
}
else
{
    Log.Information("OpenSearch security disabled");
}

if (isDevelopment)
{
    connectionSettings.DisableDirectStreaming().EnableDebugMode();
}

builder.Services.AddSingleton<IOpenSearchClient>(new OpenSearchClient(connectionSettings));

var rabbitMqHost = ConfigurationHelper.ResolveEnvironmentVariables(builder.Configuration["RabbitMQ:Host"] ?? "localhost");
var rabbitMqUser = ConfigurationHelper.ResolveEnvironmentVariables(builder.Configuration["RabbitMQ:Username"] ?? "admin");
var rabbitMqPass = ConfigurationHelper.ResolveEnvironmentVariables(builder.Configuration["RabbitMQ:Password"] ?? "");
var rabbitMqPort = builder.Configuration.GetValue<int>("RabbitMQ:Port", 5672);
Log.Information("RabbitMQ configured: Host={Host}, Port={Port}, User={User}", rabbitMqHost, rabbitMqPort, rabbitMqUser);

builder.Services.AddSingleton<RabbitMqService>(sp =>
    new RabbitMqService(
        sp.GetRequiredService<ILogger<RabbitMqService>>(),
        rabbitMqHost, rabbitMqUser, rabbitMqPass, rabbitMqPort
    ));

builder.Services.AddScoped<OpenSearchService>();
builder.Services.AddHttpClient<INlpService, NlpService>();
builder.Services.AddScoped<ISearchService>(sp =>
    new CachedSearchService(
        sp.GetRequiredService<OpenSearchService>(),
        sp.GetService<IDistributedCache>()!,
        sp.GetRequiredService<ILogger<CachedSearchService>>(),
        sp.GetRequiredService<IConfiguration>()
    )
);

builder.Services.AddScoped<DocumentChunkingService>();

builder.Services.AddSingleton<IPipelineEventService, PipelineEventService>();

var redisEnabledRaw = ConfigurationHelper.ResolveEnvironmentVariables(builder.Configuration["Redis:Enabled"] ?? "true");
var redisEnabled = bool.TryParse(redisEnabledRaw, out var rEn) && rEn;
var redisConnectionStrRaw = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
var redisConnectionStr = ConfigurationHelper.ResolveEnvironmentVariables(redisConnectionStrRaw);

if (redisEnabled)
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionStr;
        options.InstanceName = "ged:";
    });
    Log.Information("Redis cache configured: {Conn}", redisConnectionStr);
}
else
{
    builder.Services.AddDistributedMemoryCache();
    Log.Information("Redis disabled — using in-memory cache");
}

var concurrencyLimit = builder.Configuration.GetValue<int>("Indexing:ConcurrencyLimit", 4);
var batchSize = builder.Configuration.GetValue<int>("Indexing:BatchSize", 10);

Log.Information("Indexing Worker starting with concurrency={Concurrency}, batchSize={BatchSize}",
    concurrencyLimit, batchSize);

builder.Services.AddHostedService(sp => new IndexingWorkerService(
    sp,
    sp.GetRequiredService<ILogger<IndexingWorkerService>>(),
    sp.GetRequiredService<IConfiguration>(),
    pipelineEventService: sp.GetService<IPipelineEventService>(),
    rabbitHost: rabbitMqHost, rabbitUser: rabbitMqUser, rabbitPass: rabbitMqPass,
    concurrencyLimit, batchSize
));

var host = builder.Build();

Log.Information("GED Indexing Worker starting...");
Log.Information("OpenSearch: {Url}", opensearchUrl);
Log.Information("RabbitMQ: {Host}", rabbitMqHost);

await host.RunAsync();

public partial class Program { }

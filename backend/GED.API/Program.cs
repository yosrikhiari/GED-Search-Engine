using AspNetCoreRateLimit;
using GED.API.Middleware;
using GED.API.Services;
using GED.API.Authorization;
using GED.Infrastructure.Resilience;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using HealthChecks.UI.Client;
using RabbitMQ.Client;
using Microsoft.AspNetCore.Authentication.Cookies;
using GED.Core.Interfaces;
using GED.Infrastructure.Data;
using GED.Infrastructure.HealthChecks;
using GED.Infrastructure.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OpenSearch.Client;
using OpenSearch.Net;
using Serilog;
using Polly;
using Polly.CircuitBreaker;
using GED.Core.Utilities;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ───────────────────────────────────────────────────────────────────
var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDirectory);

var logIsProduction = builder.Environment.IsProduction();
var isRunningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

var loggerConfig = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "GED.API")
    .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName);

if (logIsProduction || isRunningInContainer)
{
    Log.Logger = loggerConfig
        .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter())
        .WriteTo.File(Path.Combine(logDirectory, "ged-.json"), 
            rollingInterval: RollingInterval.Day)
        .CreateLogger();
}
else
{
    Log.Logger = loggerConfig
        .WriteTo.Console()
        .WriteTo.File(Path.Combine(logDirectory, "ged-.txt"), 
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();
}

builder.Host.UseSerilog();

// ── OpenTelemetry Metrics ──────────────────────────────────────────────────────
// KNOWN LIMITATION: OpenTelemetry metrics are commented out pending package version verification
// Tracking: Waiting on OpenTelemetry.Extensions.Hosting and related packages to be validated for production use
// builder.Services.AddOpenTelemetry()
//     .ConfigureResource(resource => resource.AddService("GED.API"))
//     .WithMetrics(metrics => metrics
//         .AddMeter("GED.API")
//         .AddMeter("GED.Infrastructure")
//         .AddHttpClientInstrumentation()
//         .AddAspNetCoreInstrumentation());

builder.Services.AddSingleton<IRabbitMqStatusProvider, RabbitMqStatusProvider>();
builder.Services.AddSingleton<MetricsRegistry>();

// ── Controllers & Swagger ─────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// ── Rate Limiting ─────────────────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(options =>
{
    options.EnableEndpointRateLimiting = true;
    options.StackBlockedRequests       = false;
    options.HttpStatusCode             = 429;
    options.RealIpHeader               = "X-Real-IP";
    options.GeneralRules = new List<RateLimitRule>
    {
        new() { Endpoint = "POST:/api/auth/login",       Period = "1m", Limit = 10  },
        new() { Endpoint = "POST:/api/rag/ask",          Period = "1m", Limit = 20  },
        new() { Endpoint = "POST:/api/documents/upload", Period = "1m", Limit = 30  },
        new() { Endpoint = "*",                          Period = "1s", Limit = 100 },
    };
});
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
builder.Services.AddInMemoryRateLimiting();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "GED Search Engine API",
        Version     = "v1",
        Description = "Electronic Document Management System with OCR, NLP, and RAG capabilities"
    });
});

// ── CORS ──────────────────────────────────────────────────────────────────────
var corsOrigins = builder.Configuration["Cors:Origins"] ?? "http://localhost:3000,http://localhost:3001,http://localhost:5173";
var corsOriginList = corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries)
    .Select(o => o.Trim())
    .ToArray();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOriginList)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()
              .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});

Log.Information("CORS configured with origins: {Origins}", string.Join(", ", corsOriginList));

// ── Routing ───────────────────────────────────────────────────────────────────
builder.Services.Configure<RouteOptions>(options =>
{
    options.LowercaseUrls         = true;
    options.LowercaseQueryStrings = false;
});

// ── Request body size limit ───────────────────────────────────────────────────
var maxUploadMb = builder.Configuration.GetValue<int>("Document:MaxUploadSizeMB", 100);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = (long)maxUploadMb * 1024 * 1024;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = (long)maxUploadMb * 1024 * 1024;
});

// ── Required Configuration Validation ───────────────────────────────────────────
var isDevelopment = builder.Environment.IsDevelopment();
if (builder.Environment.EnvironmentName != "Testing")
{
    ValidateRequiredConfiguration(builder.Configuration, isDevelopment);
}

// ── SQL Server / EF Core ──────────────────────────────────────────────────────
var rawConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Resolve environment variable placeholders like ${VAR_NAME}
var connectionString = ConfigurationHelper.ResolveEnvironmentVariables(rawConnectionString ?? "");

// Validate required configuration - fail fast if not set (skip in Testing environment)
if (string.IsNullOrWhiteSpace(connectionString) && builder.Environment.EnvironmentName != "Testing")
{
    throw new InvalidOperationException(
        "FATAL: ConnectionStrings__DefaultConnection is not set. " +
        "Please configure the database connection via environment variable. " +
        "Example: ConnectionStrings__DefaultConnection='Server=myserver;Database=ged_db;User Id=myuser;Password=mypassword;TrustServerCertificate=True;'");
}

// Add connection pooling parameters to connection string
if (!connectionString.Contains("Pooling=", StringComparison.OrdinalIgnoreCase))
{
    var separator = connectionString.Contains(';') ? ";" : "";
    connectionString += $"{separator}Pooling=true;Min Pool Size=5;Max Pool Size=100;Connection Timeout=30;";
}

Log.Information("Database connection string configured (password masked): {ConnStr}", ConfigurationHelper.MaskConnectionString(connectionString));

// Register DbContext with both Scoped (for DI) and Factory (for singleton services like AuthService)
builder.Services.AddDbContext<GedDbContext>(options =>
    options.UseSqlServer(connectionString, sqlServer =>
    {
        sqlServer.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), null);
    })
);

// ── Cookie authentication ─────────────────────────────────────────────────────
var cookieIsProduction = builder.Environment.IsProduction();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name     = "ged_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.Cookie.Domain = null;
        // Only set Secure flag in production (HTTPS)
        options.Cookie.SecurePolicy = cookieIsProduction ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan  = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = 403;
            return Task.CompletedTask;
        };
    });

Log.Information("Cookie security: SameSite=Lax, SecurePolicy={SecurePolicy} (Production={IsProd})", 
    cookieIsProduction ? "Always" : "SameAsRequest", cookieIsProduction);

// ── Authorization Policies ─────────────────────────────────────────────────────
builder.Services.AddScoped<IAuthorizationHandler, CategoryAccessHandler>();
builder.Services.AddScoped<IUserContextProvider, AuthServiceUserContextProvider>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CategoryAccess", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new CategoryAccessRequirement("category"));
    });
    
    options.AddPolicy("CategoryUpload", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new CategoryAccessRequirement("category"));
    });
});

// ── OpenSearch ────────────────────────────────────────────────────────────────
var opensearchUrlRaw      = builder.Configuration["OpenSearch:Url"] ?? "http://localhost:9200";
var opensearchUrl = ConfigurationHelper.ResolveEnvironmentVariables(opensearchUrlRaw);
var opensearchUsername = ConfigurationHelper.ResolveEnvironmentVariables(builder.Configuration["OpenSearch:Username"] ?? "");
var opensearchPassword = ConfigurationHelper.ResolveEnvironmentVariables(builder.Configuration["OpenSearch:Password"] ?? "");
var opensearchSecurityEnabledRaw = ConfigurationHelper.ResolveEnvironmentVariables(builder.Configuration["OpenSearch:SecurityEnabled"] ?? "false");
var opensearchSecurityEnabled = bool.TryParse(opensearchSecurityEnabledRaw, out var osSec) && osSec;

// ── Embedding Configuration ─────────────────────────────────────────────────────
// Embedding model and dimensions must match. Changing the model requires a full reindex.
// Current models:
// - bge-m3: 1024 dimensions (default)
// - nomic-embed-text: 768 dimensions
var embedModel = builder.Configuration["Embeddings:Model"] ?? "bge-m3";
var embedDimensions = builder.Configuration.GetValue<int>("Embeddings:Dimensions", 1024);

if (embedModel != "bge-m3")
{
    Log.Warning("⚠️  EMBEDDING MODEL CHANGED to '{Model}' ({Dimensions} dimensions). " +
                "A FULL REINDEX is required! Existing vectors will not match queries. " +
                "Consider running: POST /api/admin/reindex-all",
                embedModel, embedDimensions);
}

Log.Information("Embedding model: {Model}, dimensions: {Dimensions}", embedModel, embedDimensions);

if (string.IsNullOrWhiteSpace(opensearchUrl))
{
    opensearchUrl = "http://localhost:9200";
}

var connectionSettings = new ConnectionSettings(new Uri(opensearchUrl))
    .DefaultIndex("ged-documents")
    .PrettyJson();

// Configure authentication if security is enabled
if (opensearchSecurityEnabled && !string.IsNullOrEmpty(opensearchUsername))
{
    connectionSettings.BasicAuthentication(opensearchUsername, "****"); // Don't log password
    Log.Information("OpenSearch security enabled with user: {Username}", opensearchUsername);
}
else
{
    Log.Information("OpenSearch security disabled (development mode)");
}

// Only enable debug mode in development (memory-intensive)
if (isDevelopment)
{
    connectionSettings
        .DisableDirectStreaming()
        .EnableDebugMode();
}

builder.Services.AddSingleton<IOpenSearchClient>(new OpenSearchClient(connectionSettings));

// ── RabbitMQ ──────────────────────────────────────────────────────────────────
var rabbitMqHost = ConfigurationHelper.ResolveEnvironmentVariables(builder.Configuration["RabbitMQ:Host"] ?? "localhost");
var rabbitMqUser = ConfigurationHelper.ResolveEnvironmentVariables(builder.Configuration["RabbitMQ:Username"] ?? "admin");
var rabbitMqPass = ConfigurationHelper.ResolveEnvironmentVariables(builder.Configuration["RabbitMQ:Password"] ?? "");
var rabbitMqPortRaw = ConfigurationHelper.ResolveEnvironmentVariables(builder.Configuration["RabbitMQ:Port"] ?? "5672");
var rabbitMqPort = int.TryParse(rabbitMqPortRaw, out var port) ? port : 5672;
Log.Information("RabbitMQ configured: Host={Host}, Port={Port}, User={User}", rabbitMqHost, rabbitMqPort, rabbitMqUser);

builder.Services.AddSingleton<RabbitMqService>(sp =>
    new RabbitMqService(
        sp.GetRequiredService<ILogger<RabbitMqService>>(),
        rabbitMqHost, rabbitMqUser, rabbitMqPass, rabbitMqPort
    ));
builder.Services.AddSingleton<IMessageQueueService>(sp =>
    sp.GetRequiredService<RabbitMqService>());

// ── RabbitMQ Queue Declaration (idempotent, ensures queues exist on startup) ───
builder.Services.AddHostedService<RabbitMqQueueInitService>();

// ── Redis ─────────────────────────────────────────────────────────────────────
var redisEnabledRaw = ConfigurationHelper.ResolveEnvironmentVariables(builder.Configuration["Redis:Enabled"] ?? "true");
var redisEnabled = bool.TryParse(redisEnabledRaw, out var rEn) && rEn;
var redisConnectionStrRaw = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
var redisConnectionStr = ConfigurationHelper.ResolveEnvironmentVariables(redisConnectionStrRaw);

if (redisEnabled)
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionStr;
        options.InstanceName  = "ged:";
    });
    Log.Information("✅ Redis cache configured: {Conn}", redisConnectionStr);
}
else
{
    builder.Services.AddDistributedMemoryCache();
    Log.Information("⚠️  Redis disabled — using in-memory distributed cache");
}

// ── Text Extraction ───────────────────────────────────────────────────────────
builder.Services.AddScoped<TextExtractionService>();
builder.Services.AddHttpClient<TikaTextExtractionService>()
    .AddPolicyHandler((serviceProvider, request) =>
    {
        return Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(response => (int)response.StatusCode >= 500)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, breakDuration) =>
                {
                    Log.Warning(
                        "Tika circuit breaker OPEN for {Duration}s due to: {Failure}",
                        breakDuration.TotalSeconds, 
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                },
                onReset: () => Log.Information("Tika circuit breaker RESET - resuming normal operation"),
                onHalfOpen: () => Log.Information("Tika circuit breaker HALF-OPEN - testing connection"));
    });
builder.Services.AddScoped<ITextExtractionService>(sp =>
{
    var fallback = sp.GetRequiredService<TextExtractionService>();
    var http     = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TikaTextExtractionService));
    var logger   = sp.GetRequiredService<ILogger<TikaTextExtractionService>>();
    var config   = sp.GetRequiredService<IConfiguration>();
    return new TikaTextExtractionService(http, logger, fallback, config);
});

// ── Application services ──────────────────────────────────────────────────────
var ollamaPolicy = OllamaResiliencePolicies.Combined(timeoutSeconds: 90);

builder.Services.AddScoped<IStorageService, LocalStorageService>();
builder.Services.AddScoped<IDocumentStorageService, DocumentStorageService>();
builder.Services.AddSingleton<IConfigurationService>(sp => 
    new ConfigurationService(sp.GetRequiredService<IConfiguration>()));

builder.Services.AddHttpClient<NlpService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddPolicyHandler(OllamaResiliencePolicies.Combined(timeoutSeconds: 30));
builder.Services.AddScoped<INlpService>(sp => sp.GetRequiredService<NlpService>());

builder.Services.AddHttpClient<DocumentDateExtractor>().AddPolicyHandler(ollamaPolicy);
builder.Services.AddScoped<DocumentDateExtractor>();

builder.Services.AddHttpClient<OcrTextCleaningService>()
    .AddPolicyHandler(OllamaResiliencePolicies.ForOcrCleaning());
builder.Services.AddScoped<OcrTextCleaningService>();

builder.Services.AddHttpClient<OcrMetadataEnrichmentService>().AddPolicyHandler(ollamaPolicy);
builder.Services.AddScoped<OcrMetadataEnrichmentService>();

builder.Services.AddScoped<DocumentChunkingService>();

// ── RAG Reranker Service ─────────────────────────────────────────────────────
builder.Services.AddScoped<IChunkRerankerService, ChunkRerankerService>();

// ── RAG Query Classifier Service ─────────────────────────────────────────────
builder.Services.AddScoped<IQueryClassifierService, QueryClassifierService>();

// ── Auth Service ──────────────────────────────────────────────────────────────
builder.Services.AddScoped<IUserRepository, UserRepository>();

builder.Services.AddSingleton<AuthService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<AuthService>>();
    var config = sp.GetRequiredService<IConfiguration>();
    var cache = sp.GetService<IDistributedCache>(); // Nullable - AuthService handles null
    var dbFactory = sp.GetService<IDbContextFactory<GedDbContext>>(); // Nullable - uses file fallback
    
    return new AuthService(logger, config, cache, dbFactory);
});
builder.Services.AddSingleton<IAuthService>(sp => sp.GetRequiredService<AuthService>());
builder.Services.AddSingleton<IUserContext>(sp => sp.GetRequiredService<AuthService>());

// Initialize AuthService at startup (via hosted service)
builder.Services.AddHostedService<AuthInitializationHostedService>();

// ── Audit Service ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<IAuditService, AuditService>();

// ── Search pipeline ───────────────────────────────────────────────────────────
// OpenSearchService is Scoped so it can receive GedDbContext (also Scoped).
// GedDbContext is auto-injected by the DI container — no manual wiring needed.
builder.Services.AddScoped<OpenSearchService>();
builder.Services.AddSingleton<IPipelineEventService, PipelineEventService>();
builder.Services.AddScoped<ISearchService>(sp =>
{
    var opensearch = sp.GetRequiredService<OpenSearchService>();
    var cache      = sp.GetRequiredService<IDistributedCache>();
    var logger     = sp.GetRequiredService<ILogger<CachedSearchService>>();
    var config     = sp.GetRequiredService<IConfiguration>();
    return new CachedSearchService(opensearch, cache, logger, config);
});

// ── RAG Service ───────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<RagService>().AddPolicyHandler(ollamaPolicy);
builder.Services.AddScoped<IRagService>(sp =>
    new RagService(
        sp.GetRequiredService<ISearchService>(),
        sp.GetRequiredService<OpenSearchService>(),
        sp.GetRequiredService<AuthService>(),
        sp.GetRequiredService<IChunkRerankerService>(),
        sp.GetRequiredService<IQueryClassifierService>(),
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(RagService)),
        sp.GetRequiredService<ILogger<RagService>>(),
        sp.GetRequiredService<IConfiguration>()
    ));

// ── OCR Service ───────────────────────────────────────────────────────────────
builder.Services.AddScoped<IOcrService>(sp => new OcrmyPdfOcrService(
    sp.GetRequiredService<ILogger<OcrmyPdfOcrService>>(),
    sp.GetRequiredService<IMessageQueueService>(),
    builder.Configuration["OCR:OcrmypdfPath"] ?? "ocrmypdf"
));

// ImageOcrService - uses ocrmypdf directly on images (replaces standalone Tesseract)
builder.Services.AddScoped<ImageOcrService>(sp =>
    new ImageOcrService(
        sp.GetRequiredService<ILogger<ImageOcrService>>(),
        builder.Configuration["OCR:OcrmypdfPath"] ?? "ocrmypdf"
    ));

builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IDocumentMapper, DocumentMapper>();

// ── Background workers ────────────────────────────────────────────────────────
// Determine if this is a dedicated worker instance or API server
var workerType = builder.Configuration["Worker:Type"] ?? "api";

if (workerType == "ocr")
{
    // Dedicated OCR worker instance - only run OCR worker (not OutboxRelay)
    // This allows horizontal scaling of OCR processing
    builder.Services.AddHostedService(sp => new OcrWorkerService(
        sp,
        sp.GetRequiredService<ILogger<OcrWorkerService>>(),
        builder.Configuration,
        pipelineEventService: sp.GetService<IPipelineEventService>(),
        rabbitHost: rabbitMqHost, rabbitUser: rabbitMqUser, rabbitPass: rabbitMqPass
    ));
    Log.Information("🏭 Starting in OCR Worker mode (prefetchCount=4, scalable)");
}
else
{
    // API server - run OutboxRelay for message delivery
    // Maintenance services (AutoReindex, DocumentExpiration) run in dedicated scheduler container
    // OCR workers and Indexing workers run in their own containers
    builder.Services.AddHostedService<OutboxRelayService>();
    Log.Information("Starting in API mode with OutboxRelay (maintenance services run separately)");
}
builder.Services.AddHttpClient("webhook", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<IWebhookService, WebhookService>();
builder.Services.AddScoped<DocumentIngestionPipeline>();

// ── Health Checks ─────────────────────────────────────────────────────────────
var sqlConnectionStr = connectionString;
var redisConnection  = redisConnectionStr;

// ── RabbitMQ Connection (async, non-blocking) ─────────────────────────────────
builder.Services.AddSingleton<RabbitMqConnectionService>();
builder.Services.AddHostedService<RabbitMqConnectionService>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<RabbitMqConnectionService>().Connection!);

// Register health checks
builder.Services.AddHealthChecks()
    .AddSqlServer(sqlConnectionStr, name: "sqlserver", tags: new[] { "db", "critical" },
        timeout: TimeSpan.FromSeconds(3))
    .AddRedis(redisConnection, name: "redis", tags: new[] { "cache" },
        timeout: TimeSpan.FromSeconds(2))
    .AddCheck("opensearch", () =>
    {
        try
        {
            using var client = new HttpClient();
            var response = client.GetAsync(opensearchUrl + "/_cluster/health").Result;
            if (response.IsSuccessStatusCode)
                return HealthCheckResult.Healthy("OpenSearch is healthy");
            return HealthCheckResult.Unhealthy("OpenSearch returned: " + response.StatusCode);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("OpenSearch check failed: " + ex.Message);
        }
    }, tags: new[] { "search" }, timeout: TimeSpan.FromSeconds(5));

// =============================================================================
var app = builder.Build();
// =============================================================================

// Register RabbitMQ backpressure health check after app.Build() to avoid BuildServiceProvider()
if (builder.Environment.EnvironmentName != "Testing")
{
    using (var scope = app.Services.CreateScope())
    {
        var rabbitConnService = scope.ServiceProvider.GetRequiredService<RabbitMqConnectionService>();
        var rabbitLogger = scope.ServiceProvider.GetRequiredService<ILogger<RabbitMqBackpressureHealthCheck>>();
        
        app.Services.GetRequiredService<IHealthChecksBuilder>()
            .AddCheck("rabbitmq-backpressure", new RabbitMqBackpressureHealthCheck(
                () => rabbitConnService.Connection,
                rabbitLogger
            ), tags: new[] { "messaging", "backpressure" }, timeout: TimeSpan.FromSeconds(10));
    }
}

// ── Database Migration (handles both fresh DB and existing init_schema.sql DB) ──
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GedDbContext>();
    
    // Step 1: Ensure missing columns exist (for DBs created by init_schema.sql)
    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('documents') AND name = 'DateConfidenceScore')
            ALTER TABLE documents ADD DateConfidenceScore real NULL;
        
        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('outbox_messages') AND name = 'priority')
            ALTER TABLE outbox_messages ADD priority int NULL;
        
        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('documents') AND name = 'file_hash')
            ALTER TABLE documents ADD file_hash nvarchar(64) NULL;
    ");
    
    // Step 2: Ensure missing tables exist
    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'document_versions')
        BEGIN
            CREATE TABLE document_versions (
                id uniqueidentifier NOT NULL PRIMARY KEY,
                document_id uniqueidentifier NOT NULL,
                version_number int NOT NULL,
                title nvarchar(500) NULL,
                description nvarchar(2000) NULL,
                file_name nvarchar(500) NULL,
                file_size bigint NULL,
                content_type nvarchar(200) NULL,
                category nvarchar(200) NULL,
                tags nvarchar(max) NULL,
                metadata nvarchar(max) NULL,
                changed_by nvarchar(500) NULL,
                change_reason nvarchar(1000) NULL,
                created_at datetime2 NOT NULL,
                file_path nvarchar(max) NULL
            );
            CREATE UNIQUE INDEX ix_versions_doc_version ON document_versions(document_id, version_number);
            CREATE INDEX ix_versions_document_id ON document_versions(document_id);
        END
        
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'processing_history')
        BEGIN
            CREATE TABLE processing_history (
                id uniqueidentifier NOT NULL PRIMARY KEY,
                document_id uniqueidentifier NOT NULL,
                timestamp datetime2 NOT NULL,
                stage nvarchar(100) NOT NULL,
                status nvarchar(50) NOT NULL,
                message nvarchar(max) NULL,
                duration_ms bigint NULL
            );
            CREATE INDEX ix_processing_history_doc ON processing_history(document_id, timestamp);
        END
        
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'users')
        BEGIN
            CREATE TABLE users (
                id uniqueidentifier NOT NULL PRIMARY KEY,
                username nvarchar(100) NOT NULL,
                password_hash nvarchar(max) NOT NULL,
                full_name nvarchar(200) NULL,
                email nvarchar(200) NULL,
                is_active bit NOT NULL,
                created_at datetime2 NOT NULL,
                last_login_at datetime2 NULL,
                role nvarchar(max) NOT NULL,
                allowed_categories nvarchar(max) NULL
            );
            CREATE UNIQUE INDEX ix_users_username ON users(username);
        END
        
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'webhook_deliveries')
        BEGIN
            CREATE TABLE webhook_deliveries (
                Id uniqueidentifier NOT NULL PRIMARY KEY,
                WebhookConfigId uniqueidentifier NULL,
                Event nvarchar(max) NOT NULL,
                Payload nvarchar(max) NULL,
                ResponseStatusCode int NULL,
                ResponseBody nvarchar(max) NULL,
                AttemptNumber int NOT NULL,
                Succeeded bit NOT NULL,
                ErrorMessage nvarchar(max) NULL,
                DurationMs bigint NOT NULL,
                CreatedAt datetime2 NOT NULL
            );
        END
    ");
    
    // Step 3: Ensure __EFMigrationsHistory table exists and seed it if empty
    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '__EFMigrationsHistory')
        BEGIN
            CREATE TABLE __EFMigrationsHistory (
                MigrationId nvarchar(150) NOT NULL PRIMARY KEY,
                ProductVersion nvarchar(32) NOT NULL
            );
        END
        
        IF NOT EXISTS (SELECT * FROM __EFMigrationsHistory)
        BEGIN
            INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES 
                ('20260313104638_InitialCreate', '8.0.0');
            INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES 
                ('20260403081357_AddDateConfidenceScore', '8.0.0');
            INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES 
                ('20260403085805_AddFileHashUniqueIndex', '8.0.0');
        END
    ");
    
    Log.Information("✅ Database schema validated and synchronized");
}
catch (Exception ex)
{
    Log.Error(ex, "❌ Database migration/validation failed.");
}

// ── OpenSearch index init ─────────────────────────────────────────────────────
try
{
    var client = app.Services.GetRequiredService<IOpenSearchClient>();

    await client.LowLevel.DoRequestAsync<StringResponse>(
        OpenSearch.Net.HttpMethod.PUT,
        "/_cluster/settings",
        CancellationToken.None,
        PostData.String("{\"persistent\":{\"knn.algo_param.ef_search\":100}}")
    );

    // ── ged-documents ─────────────────────────────────────────────────────────
    var indexExists = await client.Indices.ExistsAsync("ged-documents");
    if (!indexExists.Exists)
    {
        var createIndexResponse = await client.Indices.CreateAsync("ged-documents", c => c
            .Settings(s => s
                .NumberOfShards(1)
                .NumberOfReplicas(0)
                .Setting("index.knn", true)
            )
            .Map<DocumentIndexModel>(m => m
                .Properties(p => p
                    .Text(t => t.Name(n => n.Title).Analyzer("standard")
                        .Fields(f => f.Keyword(k => k.Name("keyword"))))
                    .Text(t => t.Name(n => n.Description).Analyzer("standard"))
                    .Text(t => t.Name(n => n.ExtractedText).Analyzer("standard"))
                    .Text(t => t.Name(n => n.OcrText).Analyzer("standard"))
                    .Text(t => t.Name(n => n.Category).Analyzer("standard")
                        .Fields(f => f.Keyword(k => k.Name("keyword"))))
                    .Keyword(k => k.Name(n => n.Tags))
                    .Keyword(k => k.Name(n => n.ContentType))
                    .Text(t => t.Name(n => n.FileName).Analyzer("simple")
                        .Fields(f => f.Keyword(k => k.Name("keyword"))))
                    .Keyword(k => k.Name(n => n.Status))
                    .Boolean(b => b.Name(nn => nn.IsOcrProcessed))
                    .Number(n => n.Name(nn => nn.FileSize).Type(NumberType.Long))
                    .Date(d => d.Name(n => n.CreatedAt))
                    .Date(d => d.Name(n => n.DocumentDate))
                    .Date(d => d.Name(n => n.ModifiedAt))
                    // ACL fields for tag-based and user-based access control
                    .Keyword(k => k.Name("accessLevel"))        // "open" | "restricted"
                    .Keyword(k => k.Name("allowedUserIds"))     // list of user Guids
                    .Keyword(k => k.Name("createdByUserId"))    // uploader username/id
                    .KnnVector(k => k
                        .Name("embedding")
                        .Dimension(embedDimensions)
                        .Method(mm => mm
                            .Name("hnsw")
                            .SpaceType("cosinesimil")
                            .Engine("lucene")
                            .Parameters(p => p
                                .Parameter("ef_construction", 128)
                                .Parameter("m", 16)
                            )
                        )
                    )
                )
            )
        );

        Log.Information(createIndexResponse.IsValid
            ? "✅ OpenSearch index 'ged-documents' created (ACL fields included)"
            : "❌ Failed to create index: {Error}", createIndexResponse.DebugInformation);
    }
    else
    {
        Log.Information("OpenSearch index 'ged-documents' already exists — skipping creation");

        // ── Add ACL fields to existing index via mapping update ───────────────
        // Safe to call even if the fields already exist; OpenSearch is idempotent.
        var putMappingResponse = await client.Indices.PutMappingAsync<DocumentIndexModel>(pm => pm
            .Index("ged-documents")
            .Properties(p => p
                .Keyword(k => k.Name("accessLevel"))
                .Keyword(k => k.Name("allowedUserIds"))
                .Keyword(k => k.Name("createdByUserId"))
                .Boolean(b => b.Name(nn => nn.IsOcrProcessed))
            )
        );

        Log.Information(putMappingResponse.IsValid
            ? "✅ ACL fields added/confirmed in 'ged-documents' mapping"
            : "⚠️  Mapping update for ACL fields returned: {Error}",
              putMappingResponse.DebugInformation);
    }

    // ── ged-chunks ────────────────────────────────────────────────────────────
    var chunksExists = await client.Indices.ExistsAsync("ged-chunks");
    if (!chunksExists.Exists)
    {
        var createChunksResponse = await client.Indices.CreateAsync("ged-chunks", c => c
            .Settings(s => s
                .NumberOfShards(1)
                .NumberOfReplicas(0)
                .Setting("index.knn", true)
            )
            .Map(m => m
                .Properties(p => p
                    .Keyword(k => k.Name("document_id"))
                    .Keyword(k => k.Name("chunk_id"))
                    .Number(n => n.Name("chunk_index").Type(NumberType.Integer))
                    .Text(t => t.Name("text").Analyzer("standard"))
                    .Text(t => t.Name("title").Analyzer("standard"))
                    .Text(t => t.Name("category").Analyzer("standard")
                        .Fields(f => f.Keyword(k => k.Name("keyword"))))
                    .Date(d => d.Name("document_date"))
                    .Date(d => d.Name("created_at"))
                    .Keyword(k => k.Name("file_name"))
                    .Keyword(k => k.Name("content_type"))
                    .Keyword(k => k.Name("tags"))
                    .KnnVector(k => k
                        .Name("embedding")
                        .Dimension(embedDimensions)
                        .Method(mm => mm
                            .Name("hnsw")
                            .SpaceType("cosinesimil")
                            .Engine("lucene")
                            .Parameters(pp => pp
                                .Parameter("ef_construction", 128)
                                .Parameter("m", 16)
                            )
                        )
                    )
                )
            )
        );

        Log.Information(createChunksResponse.IsValid
            ? "✅ OpenSearch index 'ged-chunks' created"
            : "❌ Failed to create ged-chunks index: {Error}",
            createChunksResponse.DebugInformation);
    }
    else
    {
        Log.Information("OpenSearch index 'ged-chunks' already exists — skipping creation");
    }
}
catch (Exception ex)
{
    Log.Error(ex, "Error initializing OpenSearch indexes");
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "GED API v1");
        c.RoutePrefix = string.Empty;
    });
}

app.Use(async (context, next) =>
{
    Log.Information("HTTP {Method} {Path}", context.Request.Method, context.Request.Path);
    await next();
    Log.Information("HTTP {Method} {Path} -> {StatusCode}",
        context.Request.Method, context.Request.Path, context.Response.StatusCode);
});

app.UseIpRateLimiting();
app.UseCors();
app.UseGlobalExceptionHandler();

app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"]
        .FirstOrDefault() ?? Guid.NewGuid().ToString("N")[..12];

    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers["X-Correlation-Id"] = correlationId;

    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
    {
        await next();
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate      = _ => false,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate      = check => check.Tags.Contains("critical"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapControllers();

Log.Information("GED Search Engine API starting...");
Log.Information("OpenSearch:  {Url}", opensearchUrl);
Log.Information("RabbitMQ:   {Host}", rabbitMqHost);
Log.Information("SQL Server: {ConnStr}", ConfigurationHelper.MaskConnectionString(connectionString));

app.Run();

public partial class Program
{
    private static void ValidateRequiredConfiguration(IConfiguration configuration, bool isDevelopment)
    {
        var errors = new List<string>();
        
        var dbConn = ConfigurationHelper.ResolveEnvironmentVariables(configuration.GetConnectionString("DefaultConnection") ?? "");
        if (string.IsNullOrWhiteSpace(dbConn) && !isDevelopment)
            errors.Add("ConnectionStrings__DefaultConnection (database)");
        
        var rabbitHost = ConfigurationHelper.ResolveEnvironmentVariables(configuration["RabbitMQ:Host"] ?? "");
        if (string.IsNullOrWhiteSpace(rabbitHost) && !isDevelopment)
            errors.Add("RabbitMQ:Host");
        
        var osUrl = ConfigurationHelper.ResolveEnvironmentVariables(configuration["OpenSearch:Url"] ?? "");
        if (string.IsNullOrWhiteSpace(osUrl) && !isDevelopment)
            errors.Add("OpenSearch:Url");
        
        if (errors.Any())
        {
            throw new InvalidOperationException(
                $"FATAL: Missing required production configuration. Please set the following environment variables:\n" +
                string.Join("\n", errors.Select(e => $"  - {e}")));
        }
        
        Log.Information("Configuration validation passed");
    }
}
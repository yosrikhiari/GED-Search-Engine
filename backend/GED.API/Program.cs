using AspNetCoreRateLimit;
using GED.Infrastructure.Resilience;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using HealthChecks.UI.Client;
using RabbitMQ.Client;
using Microsoft.AspNetCore.Authentication.Cookies;
using GED.Core.Interfaces;
using GED.Infrastructure.Data;
using GED.Infrastructure.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OpenSearch.Client;
using OpenSearch.Net;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ───────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/ged-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

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

// ── Rate Limiting (ByteByteGo: Token Bucket) ──────────────────────────────────
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
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

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

// ── SQL Server / EF Core ──────────────────────────────────────────────────────
// In docker-compose, set environment variable:
//   ConnectionStrings__DefaultConnection=Server=ged-sqlserver;Database=ged_db;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=localhost;Database=ged_db;User Id=ged_user;Password=ged_pass;TrustServerCertificate=True;";

builder.Services.AddDbContext<GedDbContext>(options =>
    options.UseSqlServer(connectionString, sqlServer =>
    {
        sqlServer.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), null);
    })
);

// ── Session Based Cookies ─────────────────────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name     = "ged_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan  = TimeSpan.FromHours(8);
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

builder.Services.AddAuthorization();

// ── OpenSearch ────────────────────────────────────────────────────────────────
// In docker-compose, set environment variable:
//   OpenSearch__Url=http://ged-opensearch:9200
var opensearchUrl = builder.Configuration["OpenSearch:Url"] ?? "http://localhost:9200";
var connectionSettings = new ConnectionSettings(new Uri(opensearchUrl))
    .DefaultIndex("ged-documents")
    .DisableDirectStreaming()
    .EnableDebugMode()
    .PrettyJson();

builder.Services.AddSingleton<IOpenSearchClient>(new OpenSearchClient(connectionSettings));

// ── RabbitMQ ──────────────────────────────────────────────────────────────────
// In docker-compose, set environment variables:
//   RabbitMQ__Host=ged-rabbitmq
//   RabbitMQ__Username=admin
//   RabbitMQ__Password=admin123
var rabbitMqHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
var rabbitMqUser = builder.Configuration["RabbitMQ:Username"] ?? "admin";
var rabbitMqPass = builder.Configuration["RabbitMQ:Password"] ?? "admin123";

builder.Services.AddSingleton<RabbitMqService>(sp =>
    new RabbitMqService(
        sp.GetRequiredService<ILogger<RabbitMqService>>(),
        rabbitMqHost, rabbitMqUser, rabbitMqPass
    ));
builder.Services.AddSingleton<IMessageQueueService>(sp =>
    sp.GetRequiredService<RabbitMqService>());

// ── Redis distributed cache ───────────────────────────────────────────────────
// In docker-compose, set environment variable:
//   Redis__ConnectionString=ged-redis:6379
var redisEnabled       = builder.Configuration.GetValue<bool>("Redis:Enabled", true);
var redisConnectionStr = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";

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

// ── Text Extraction: Tika (primary) + built-in fallback ──────────────────────
builder.Services.AddScoped<TextExtractionService>();

builder.Services.AddHttpClient<TikaTextExtractionService>();
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

builder.Services.AddHttpClient<NlpService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddPolicyHandler(OllamaResiliencePolicies.Combined(timeoutSeconds: 30));
builder.Services.AddScoped<INlpService>(sp => sp.GetRequiredService<NlpService>());

builder.Services.AddHttpClient<DocumentDateExtractor>()
    .AddPolicyHandler(ollamaPolicy);
builder.Services.AddScoped<DocumentDateExtractor>();

builder.Services.AddHttpClient<OcrTextCleaningService>()
    .AddPolicyHandler(OllamaResiliencePolicies.ForOcrCleaning());
builder.Services.AddScoped<OcrTextCleaningService>();

builder.Services.AddHttpClient<OcrMetadataEnrichmentService>()
    .AddPolicyHandler(ollamaPolicy);
builder.Services.AddScoped<OcrMetadataEnrichmentService>();

// ── Chunking service ──────────────────────────────────────────────────────────
builder.Services.AddScoped<DocumentChunkingService>();

// ── Auth Service ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<AuthService>();

// ── Search pipeline ───────────────────────────────────────────────────────────
builder.Services.AddScoped<OpenSearchService>();
builder.Services.AddScoped<ISearchService>(sp =>
{
    var opensearch = sp.GetRequiredService<OpenSearchService>();
    var cache      = sp.GetRequiredService<IDistributedCache>();
    var logger     = sp.GetRequiredService<ILogger<CachedSearchService>>();
    var config     = sp.GetRequiredService<IConfiguration>();
    return new CachedSearchService(opensearch, cache, logger, config);
});

// ── RAG Service ───────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<RagService>()
    .AddPolicyHandler(ollamaPolicy);
builder.Services.AddScoped<IRagService>(sp =>
    new RagService(
        sp.GetRequiredService<ISearchService>(),
        sp.GetRequiredService<OpenSearchService>(),
        sp.GetRequiredService<AuthService>(),
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
builder.Services.AddScoped<TesseractDirectOcrService>(sp =>
    new TesseractDirectOcrService(
        sp.GetRequiredService<ILogger<TesseractDirectOcrService>>(),
        sp.GetRequiredService<IMessageQueueService>(),
        builder.Configuration["OCR:TesseractPath"] ?? "tesseract"
    ));

builder.Services.AddScoped<IDocumentService, DocumentService>();

// ── Background workers ────────────────────────────────────────────────────────
builder.Services.AddHostedService(sp => new OcrWorkerService(
    sp,
    sp.GetRequiredService<ILogger<OcrWorkerService>>(),
    rabbitMqHost, rabbitMqUser, rabbitMqPass
));

builder.Services.AddHostedService<AutoReindexService>();
builder.Services.AddHostedService<OutboxRelayService>();
builder.Services.AddScoped<DocumentIngestionPipeline>();

// ── Health Checks ─────────────────────────────────────────────────────────────
var sqlConnectionStr = connectionString;
var redisConnection  = redisConnectionStr;
var rabbitHost       = rabbitMqHost;
var rabbitUser       = rabbitMqUser;
var rabbitPass       = rabbitMqPass;

// Register a Lazy<IConnection> so the actual TCP handshake with RabbitMQ happens
// on first use (/health poll), NOT at startup. This prevents the process from
// crashing with exit code 1 when RabbitMQ isn't yet accepting connections.
// Fully qualify RabbitMQ.Client.IConnection to avoid ambiguity with OpenSearch.Net.IConnection.
builder.Services.AddSingleton(new Lazy<RabbitMQ.Client.IConnection>(() =>
{
    var factory = new ConnectionFactory
    {
        Uri = new Uri($"amqp://{rabbitUser}:{rabbitPass}@{rabbitHost}")
    };
    // CreateConnectionAsync returns Task<IConnection> — block here intentionally
    // (Lazy<T> factory must be synchronous; this runs only once, lazily at first /health poll).
    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
}));

builder.Services.AddHealthChecks()
    .AddSqlServer(
        sqlConnectionStr,
        name: "sqlserver",
        tags: new[] { "db", "critical" },
        timeout: TimeSpan.FromSeconds(3))
    .AddRedis(
        redisConnection,
        name: "redis",
        tags: new[] { "cache" },
        timeout: TimeSpan.FromSeconds(2))
    // FIX 2: The original code called factory.CreateConnectionAsync().GetAwaiter().GetResult()
    //         INSIDE the AddRabbitMQ lambda, which runs during DI registration — BEFORE the app
    //         is built. If RabbitMQ isn't ready at that exact moment the process crashes (exit 1).
    //
    //         Fix: wrap the IConnection in a Lazy<T> singleton. The lambda is only executed the
    //         first time /health is actually polled, not at startup.
    .AddRabbitMQ(
        sp => sp.GetRequiredService<Lazy<RabbitMQ.Client.IConnection>>().Value,
        name: "rabbitmq",
        tags: new[] { "messaging", "critical" },
        timeout: TimeSpan.FromSeconds(5));

// =============================================================================
var app = builder.Build();
// =============================================================================

// FIX 3: Run EF Core migrations automatically on startup so the DB schema is
//         always in sync. The SQL Server container uses a healthcheck + depends_on
//         so it should be ready, but the retry policy on UseSqlServer handles
//         any remaining timing issues.
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GedDbContext>();
    Log.Information("⏳ Applying EF Core migrations...");
    await db.Database.MigrateAsync();
    Log.Information("✅ EF Core migrations applied");
}
catch (Exception ex)
{
    // Log but don't crash — the retry policy on the DbContext handles transient failures.
    Log.Error(ex, "❌ EF Core migration failed. The backend will still start; check DB connectivity.");
}

// ── OpenSearch index init ─────────────────────────────────────────────────────
// Already wrapped in try/catch — safe to keep as-is.
try
{
    var client = app.Services.GetRequiredService<IOpenSearchClient>();

    await client.LowLevel.DoRequestAsync<StringResponse>(
        OpenSearch.Net.HttpMethod.PUT,
        "/_cluster/settings",
        CancellationToken.None,
        PostData.String("{\"persistent\":{\"knn.algo_param.ef_search\":100}}")
    );

    // ── ged-documents (main BM25 + embedding index) ───────────────────────────
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
                    .Keyword(k => k.Name(n => n.FileName))
                    .Keyword(k => k.Name(n => n.Status))
                    .Number(n => n.Name(nn => nn.FileSize).Type(NumberType.Long))
                    .Date(d => d.Name(n => n.CreatedAt))
                    .Date(d => d.Name(n => n.DocumentDate))
                    .Date(d => d.Name(n => n.ModifiedAt))
                    .KnnVector(k => k
                        .Name("embedding")
                        .Dimension(768)
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
            ? "✅ OpenSearch index 'ged-documents' created with kNN vector field"
            : "❌ Failed to create index: {Error}", createIndexResponse.DebugInformation);
    }
    else
    {
        Log.Information("OpenSearch index 'ged-documents' already exists — skipping creation");
    }

    // ── ged-chunks (chunk-level RAG index) ────────────────────────────────────
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
                        .Dimension(768)
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
Log.Information("SQL Server: {ConnStr}", connectionString);

app.Run();
using GED.Core.Interfaces;
using GED.Infrastructure.Data;
using GED.Infrastructure.Services;
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
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "GED Search Engine API",
        Version     = "v1",
        Description = "Electronic Document Management System with NLP and OCR capabilities"
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
    options.LowercaseUrls          = true;
    options.LowercaseQueryStrings  = false;
});

// ── FIX #4: Enforce request body size limit from config ───────────────────────
// This sets the Kestrel/IIS limit so oversized uploads are rejected at the
// transport level before they even reach the controller.
var maxUploadMb = builder.Configuration.GetValue<int>("Document:MaxUploadSizeMB", 100);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = (long)maxUploadMb * 1024 * 1024;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = (long)maxUploadMb * 1024 * 1024;
});

// ── FIX #1: PostgreSQL with EF Core ──────────────────────────────────────────
// Previously documents were stored as flat JSON files that were lost on restart.
// Now they're persisted in PostgreSQL.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Database=ged_db;Username=ged_user;Password=ged_pass;Port=5432";

builder.Services.AddDbContext<GedDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), null);
    })
);

// ── OpenSearch ────────────────────────────────────────────────────────────────
var opensearchUrl = builder.Configuration["OpenSearch:Url"] ?? "http://localhost:9200";
var connectionSettings = new ConnectionSettings(new Uri(opensearchUrl))
    .DefaultIndex("ged-documents")
    .DisableDirectStreaming()
    .EnableDebugMode()
    .PrettyJson();

builder.Services.AddSingleton<IOpenSearchClient>(new OpenSearchClient(connectionSettings));

// ── RabbitMQ ──────────────────────────────────────────────────────────────────
var rabbitMqHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
var rabbitMqUser = builder.Configuration["RabbitMQ:Username"] ?? "admin";
var rabbitMqPass = builder.Configuration["RabbitMQ:Password"] ?? "admin123";

builder.Services.AddSingleton<IMessageQueueService>(sp =>
    new RabbitMqService(
        sp.GetRequiredService<ILogger<RabbitMqService>>(),
        rabbitMqHost,
        rabbitMqUser,
        rabbitMqPass
    ));

// ── Application Services ──────────────────────────────────────────────────────

// Step 1: Leaf services (no custom-service dependencies)
builder.Services.AddScoped<ITextExtractionService, TextExtractionService>();
builder.Services.AddScoped<IStorageService, LocalStorageService>();

// Step 2: Services that need a typed HttpClient
builder.Services.AddHttpClient<NlpService>();
builder.Services.AddScoped<INlpService>(sp => sp.GetRequiredService<NlpService>());

builder.Services.AddHttpClient<DocumentMetadataService>();
builder.Services.AddScoped<DocumentMetadataService>();

builder.Services.AddHttpClient<DocumentDateExtractor>();
builder.Services.AddScoped<DocumentDateExtractor>();

// Step 3: Services that depend on the above (DocumentService now needs GedDbContext)
builder.Services.AddScoped<ISearchService, OpenSearchService>();
builder.Services.AddScoped<IOcrService, TesseractOcrService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();

// ── FIX #2: Register the OCR background worker ────────────────────────────────
// Previously jobs were queued to RabbitMQ but nobody consumed them.
// OcrWorkerService is a BackgroundService that reads from "ocr-queue" and
// calls TesseractOcrService, then updates the DB and re-indexes in OpenSearch.
builder.Services.AddHostedService(sp => new OcrWorkerService(
    sp,
    sp.GetRequiredService<ILogger<OcrWorkerService>>(),
    rabbitMqHost,
    rabbitMqUser,
    rabbitMqPass
));

// ── Build app ─────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── FIX #1: Auto-migrate PostgreSQL on startup ────────────────────────────────
// Creates the tables if they don't exist. Safe to run repeatedly.
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GedDbContext>();
    await db.Database.MigrateAsync();
    Log.Information("✅ PostgreSQL migrations applied successfully");
}
catch (Exception ex)
{
    Log.Error(ex, "❌ Failed to apply PostgreSQL migrations");
    // Don't crash on startup — the app can still run for search-only operations
}

// ── OpenSearch index initialization ──────────────────────────────────────────
try
{
    var client = app.Services.GetRequiredService<IOpenSearchClient>();
    var indexExists = await client.Indices.ExistsAsync("ged-documents");

    if (!indexExists.Exists)
    {
        var createIndexResponse = await client.Indices.CreateAsync("ged-documents", c => c
            .Settings(s => s.NumberOfShards(1).NumberOfReplicas(0))
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
                    .Number(n => n.Name(nn => nn.FileSize).Type(NumberType.Long))
                    .Date(d => d.Name(n => n.CreatedAt))
                    .Date(d => d.Name(n => n.DocumentDate))
                    .Date(d => d.Name(n => n.ModifiedAt))
                    .Keyword(k => k.Name(n => n.Status))
                )
            )
        );

        Log.Information(createIndexResponse.IsValid
            ? "✅ OpenSearch index 'ged-documents' created"
            : "❌ Failed to create OpenSearch index: {Error}", createIndexResponse.DebugInformation);
    }
    else
    {
        Log.Information("✅ OpenSearch index 'ged-documents' already exists");
    }
}
catch (Exception ex)
{
    Log.Error(ex, "Error initializing OpenSearch index");
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
// ── ADDITIONS to Program.cs (replace the "Application Services" section) ─────
//
// This file shows ONLY the changed / added blocks.
// Everything else in Program.cs (Serilog, CORS, Swagger, Kestrel limits,
// PostgreSQL, OpenSearch client, RabbitMQ, OcrWorkerService) remains the same.
//
// ─────────────────────────────────────────────────────────────────────────────

// ── #13 Redis caching ─────────────────────────────────────────────────────────
// StackExchange.Redis backed IDistributedCache.
// When Redis is unavailable at startup the app still works — CachedSearchService
// degrades gracefully on every call.

var redisEnabled        = builder.Configuration.GetValue<bool>("Redis:Enabled", true);
var redisConnectionStr  = builder.Configuration["Redis:ConnectionString"]
                          ?? "localhost:6379";

if (redisEnabled)
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionStr;
        options.InstanceName  = "ged:";           // namespace prefix
    });
    Log.Information("✅ Redis cache configured: {Conn}", redisConnectionStr);
}
else
{
    // Fallback to in-process memory cache so IDistributedCache is always registered
    builder.Services.AddDistributedMemoryCache();
    Log.Information("⚠️  Redis disabled — using in-memory distributed cache");
}

// ── #14 Embeddings & vector search ───────────────────────────────────────────

builder.Services.AddHttpClient<OllamaEmbeddingService>();
builder.Services.AddSingleton<IEmbeddingService>(sp =>
    sp.GetRequiredService<OllamaEmbeddingService>());

builder.Services.AddScoped<VectorSearchService>();

// ── Application Services ──────────────────────────────────────────────────────

// Step 1: Leaf services (no custom-service dependencies)
builder.Services.AddScoped<ITextExtractionService, TextExtractionService>();
builder.Services.AddScoped<IStorageService, LocalStorageService>();

// Step 2: Services that need a typed HttpClient
builder.Services.AddHttpClient<NlpService>();
builder.Services.AddScoped<INlpService>(sp => sp.GetRequiredService<NlpService>());

builder.Services.AddHttpClient<DocumentMetadataService>();
builder.Services.AddScoped<DocumentMetadataService>();

builder.Services.AddHttpClient<DocumentDateExtractor>();
builder.Services.AddScoped<DocumentDateExtractor>();

// Step 3: Core search pipeline
//
//   OpenSearchService    ← keyword/BM25 (concrete class, needed by HybridSearchService)
//   HybridSearchService  ← combines keyword + vector (#14)  → registered as ISearchService
//   CachedSearchService  ← Redis cache decorator (#13)      → wraps ISearchService
//
// Registration order matters: later registrations for ISearchService WIN.

builder.Services.AddScoped<OpenSearchService>();     // concrete — injected by HybridSearchService

// #14: Hybrid search — becomes the "inner" ISearchService
builder.Services.AddScoped<HybridSearchService>();

// #13: Cache decorator — outermost layer, what controllers see
builder.Services.AddScoped<ISearchService>(sp =>
{
    var hybrid  = sp.GetRequiredService<HybridSearchService>();
    var cache   = sp.GetRequiredService<IDistributedCache>();
    var logger  = sp.GetRequiredService<ILogger<CachedSearchService>>();
    var config  = sp.GetRequiredService<IConfiguration>();
    return new CachedSearchService(hybrid, cache, logger, config);
});

builder.Services.AddScoped<IOcrService, TesseractOcrService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();

// ── Background workers ────────────────────────────────────────────────────────

// OCR consumer (existing)
builder.Services.AddHostedService(sp => new OcrWorkerService(
    sp,
    sp.GetRequiredService<ILogger<OcrWorkerService>>(),
    rabbitMqHost,
    rabbitMqUser,
    rabbitMqPass
));

// #15: Auto-reindex worker
builder.Services.AddHostedService<AutoReindexService>();

// ─────────────────────────────────────────────────────────────────────────────
// In the startup block, after OpenSearch index creation, add vector index init:
// ─────────────────────────────────────────────────────────────────────────────

// ── #14: Vector index initialization ─────────────────────────────────────────
try
{
    using var scope = app.Services.CreateScope();
    var vectorSvc   = scope.ServiceProvider.GetRequiredService<VectorSearchService>();
    await vectorSvc.EnsureIndexAsync();
}
catch (Exception ex)
{
    Log.Warning(ex, "Vector index initialization failed — semantic search unavailable");
}
app.Use(async (context, next) =>
{
    Log.Information("HTTP {Method} {Path}", context.Request.Method, context.Request.Path);
    await next();
    Log.Information("HTTP {Method} {Path} → {StatusCode}",
        context.Request.Method, context.Request.Path, context.Response.StatusCode);
});

app.UseCors();
app.UseAuthorization();
app.MapControllers();

Log.Information("GED Search Engine API starting...");
Log.Information("OpenSearch: {Url}", opensearchUrl);
Log.Information("RabbitMQ:   {Host}", rabbitMqHost);
Log.Information("PostgreSQL: {ConnStr}", connectionString);

app.Run();
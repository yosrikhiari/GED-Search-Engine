using GED.Core.Interfaces;
using GED.Infrastructure.Data;
using GED.Infrastructure.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenSearch.Client;
using OpenSearch.Net;
using Serilog;
using System.Text;

// ── CRITICAL: Npgsql 8.x UTC fix ─────────────────────────────────────────────
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

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
        Description = "Electronic Document Management System with OCR, NLP, and RAG capabilities"
    });

    // Add JWT auth to Swagger UI
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Example: \"Bearer {token}\"",
        Name        = "Authorization",
        In          = ParameterLocation.Header,
        Type        = SecuritySchemeType.ApiKey,
        Scheme      = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
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

// ── JWT Authentication ────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Auth:JwtSecret"]
    ?? "GED-SuperSecretKey-ChangeInProduction-2024!";
var jwtIssuer = builder.Configuration["Auth:JwtIssuer"] ?? "GED-SearchEngine";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer           = true,
        ValidIssuer              = jwtIssuer,
        ValidateAudience         = false,
        ValidateLifetime         = true,
        ClockSkew                = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

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

// ── PostgreSQL / EF Core ──────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Database=ged_db;Username=ged_user;Password=ged_pass;Port=5432";

var dataSource = new Npgsql.NpgsqlDataSourceBuilder(connectionString)
    .EnableDynamicJson()
    .Build();

builder.Services.AddDbContext<GedDbContext>(options =>
    options.UseNpgsql(dataSource, npgsql =>
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
        rabbitMqHost, rabbitMqUser, rabbitMqPass
    ));

// ── Redis distributed cache ───────────────────────────────────────────────────
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

// ── Embeddings & vector search ────────────────────────────────────────────────



// ── Text Extraction: Tika (primary) + built-in fallback ──────────────────────
// Register the built-in extractor first, then wrap it in TikaTextExtractionService.
// This gives Tika priority while preserving the existing extractor as fallback.
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
builder.Services.AddScoped<IStorageService, LocalStorageService>();

builder.Services.AddHttpClient<NlpService>();
builder.Services.AddScoped<INlpService>(sp => sp.GetRequiredService<NlpService>());


builder.Services.AddHttpClient<DocumentDateExtractor>();
builder.Services.AddScoped<DocumentDateExtractor>();

builder.Services.AddHttpClient<OcrTextCleaningService>();
builder.Services.AddScoped<OcrTextCleaningService>();

// ── RAG Service ───────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<RagService>();
builder.Services.AddScoped<IRagService>(sp =>
    new RagService(
        sp.GetRequiredService<ISearchService>(),
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(RagService)),
        sp.GetRequiredService<ILogger<RagService>>(),
        sp.GetRequiredService<IConfiguration>()
    ));

// ── Auth Service ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<AuthService>();

// ── Search pipeline ───────────────────────────────────────────────────────────
builder.Services.AddScoped<OpenSearchService>();
builder.Services.AddScoped<ISearchService>(sp =>
{
    var opensearch = sp.GetRequiredService<OpenSearchService>();
    var cache  = sp.GetRequiredService<IDistributedCache>();
    var logger = sp.GetRequiredService<ILogger<CachedSearchService>>();
    var config = sp.GetRequiredService<IConfiguration>();
    return new CachedSearchService(opensearch, cache, logger, config);
});

// ── OCR Service ───────────────────────────────────────────────────────────────
builder.Services.AddScoped<IOcrService>(sp => new OcrmyPdfOcrService(
    sp.GetRequiredService<ILogger<OcrmyPdfOcrService>>(),
    sp.GetRequiredService<IMessageQueueService>(),
    builder.Configuration["OCR:OcrmypdfPath"] ?? "ocrmypdf"
));

builder.Services.AddScoped<IDocumentService, DocumentService>();

// ── Background workers ────────────────────────────────────────────────────────
builder.Services.AddHostedService(sp => new OcrWorkerService(
    sp,
    sp.GetRequiredService<ILogger<OcrWorkerService>>(),
    rabbitMqHost, rabbitMqUser, rabbitMqPass
));

builder.Services.AddHostedService<AutoReindexService>();

// =============================================================================
var app = builder.Build();
// =============================================================================



// ── OpenSearch index init ─────────────────────────────────────────────────────
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

app.Use(async (context, next) =>
{
    Log.Information("HTTP {Method} {Path}", context.Request.Method, context.Request.Path);
    await next();
    Log.Information("HTTP {Method} {Path} -> {StatusCode}",
        context.Request.Method, context.Request.Path, context.Response.StatusCode);
});

app.UseCors();
app.UseAuthentication();   // ← NEW: must come before UseAuthorization
app.UseAuthorization();
app.MapControllers();

Log.Information("GED Search Engine API starting...");
Log.Information("OpenSearch: {Url}", opensearchUrl);
Log.Information("RabbitMQ:   {Host}", rabbitMqHost);
Log.Information("PostgreSQL: {ConnStr}", connectionString);

app.Run();

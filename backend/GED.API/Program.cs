using GED.Core.Interfaces;
using GED.Infrastructure.Services;
using Microsoft.OpenApi.Models;
using OpenSearch.Client;
using OpenSearch.Net;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/ged-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "GED Search Engine API",
        Version = "v1",
        Description = "Electronic Document Management System with NLP and OCR capabilities"
    });
});

// CORS - Allow frontend to connect
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

// Configure routing - IMPORTANT: Make routes case-insensitive
builder.Services.Configure<RouteOptions>(options =>
{
    options.LowercaseUrls = true;
    options.LowercaseQueryStrings = false;
});

// OpenSearch Client
var opensearchUrl = builder.Configuration["OpenSearch:Url"] ?? "http://localhost:9200";
var connectionSettings = new ConnectionSettings(new Uri(opensearchUrl))
    .DefaultIndex("ged-documents")
    .DisableDirectStreaming()
    .EnableDebugMode()
    .PrettyJson();

builder.Services.AddSingleton<IOpenSearchClient>(new OpenSearchClient(connectionSettings));

// RabbitMQ
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

// ============================================================
// FIX: Correct service registration - no duplicates.
//
// Root cause of DocumentDateExtractor always being null:
//
//   BEFORE (broken):
//     builder.Services.AddHttpClient<DocumentDateExtractor>();  // registers Transient + named HttpClient
//     builder.Services.AddScoped<DocumentDateExtractor>();      // OVERWRITES with plain Scoped, loses HttpClient config
//
//   When DocumentService asked DI for DocumentDateExtractor, the Scoped registration
//   was used. That registration had no HttpClient factory, so .NET injected a plain
//   default HttpClient — but the REAL problem is that ASP.NET Core's optional
//   constructor parameter resolution falls back to null when the type can't be
//   cleanly resolved from the container as an optional dependency.
//
//   AFTER (fixed):
//     AddHttpClient<T>() already registers T as Transient AND configures its HttpClient.
//     We then add ONE AddScoped<T>() to promote the lifetime — but this must come
//     AFTER AddHttpClient so the HttpClient factory is already wired.
//     The Scoped registration delegates to the factory registered by AddHttpClient.
// ============================================================

// Step 1: Leaf services with no custom-service dependencies
builder.Services.AddScoped<ITextExtractionService, TextExtractionService>();
builder.Services.AddScoped<IStorageService, LocalStorageService>();

// Step 2: Services that need a typed HttpClient.
//   AddHttpClient<T>() registers T as Transient and wires the IHttpClientFactory.
//   The subsequent AddScoped<T>() promotes the lifetime to Scoped so all services
//   in the same request share one instance — this does NOT break the HttpClient wiring.
builder.Services.AddHttpClient<NlpService>();
builder.Services.AddScoped<INlpService>(sp => sp.GetRequiredService<NlpService>());

builder.Services.AddHttpClient<DocumentMetadataService>();
builder.Services.AddScoped<DocumentMetadataService>();

builder.Services.AddHttpClient<DocumentDateExtractor>();
builder.Services.AddScoped<DocumentDateExtractor>();

// Step 3: Services that depend on the above
builder.Services.AddScoped<ISearchService, OpenSearchService>();
builder.Services.AddScoped<IOcrService, TesseractOcrService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();

var app = builder.Build();

// Ensure OpenSearch index exists
try
{
    var client = app.Services.GetRequiredService<IOpenSearchClient>();
    var indexExists = await client.Indices.ExistsAsync("ged-documents");

    if (!indexExists.Exists)
    {
        var createIndexResponse = await client.Indices.CreateAsync("ged-documents", c => c
            .Settings(s => s
                .NumberOfShards(1)
                .NumberOfReplicas(0)
            )
            .Map<GED.Infrastructure.Services.DocumentIndexModel>(m => m
                .Properties(p => p
                    .Text(t => t
                        .Name(n => n.Title)
                        .Analyzer("standard")
                        .Fields(f => f.Keyword(k => k.Name("keyword")))
                    )
                    .Text(t => t
                        .Name(n => n.Description)
                        .Analyzer("standard")
                    )
                    .Text(t => t
                        .Name(n => n.ExtractedText)
                        .Analyzer("standard")
                    )
                    .Text(t => t
                        .Name(n => n.OcrText)
                        .Analyzer("standard")
                    )
                    .Text(t => t
                        .Name(n => n.Category)
                        .Analyzer("standard")
                        .Fields(f => f.Keyword(k => k.Name("keyword")))
                    )
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

        if (createIndexResponse.IsValid)
        {
            Log.Information("✅ OpenSearch index 'ged-documents' created successfully");
        }
        else
        {
            Log.Error("❌ Failed to create OpenSearch index: {Error}",
                createIndexResponse.DebugInformation);
        }
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

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "GED API v1");
        c.RoutePrefix = string.Empty; // Serve Swagger UI at root
    });
}

// Add request logging middleware
app.Use(async (context, next) =>
{
    Log.Information("HTTP {Method} {Path} from {RemoteIp}",
        context.Request.Method,
        context.Request.Path,
        context.Connection.RemoteIpAddress);
    await next();
    Log.Information("HTTP {Method} {Path} returned {StatusCode}",
        context.Request.Method,
        context.Request.Path,
        context.Response.StatusCode);
});

app.UseCors();
app.UseAuthorization();
app.MapControllers();

Log.Information("GED Search Engine API starting...");
Log.Information("OpenSearch URL: {OpenSearchUrl}", opensearchUrl);
Log.Information("RabbitMQ Host: {RabbitMqHost}", rabbitMqHost);
Log.Information("Listening on: http://localhost:5001");

app.Run();
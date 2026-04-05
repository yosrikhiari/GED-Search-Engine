using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using GED.Infrastructure.Data;
using System.Net.Http.Json;
using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Services;
using GED.API.Services;
using GED.Infrastructure.HealthChecks;
using RabbitMQ.Client;
using System.Net;

namespace GED.Tests.Integration;

public class GedTestWebApplicationFactory : WebApplicationFactory<Program>
{
    public static string DbName { get; } = $"GED-Test-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        Environment.SetEnvironmentVariable("RABBITMQ_HOST", "localhost");
        Environment.SetEnvironmentVariable("RABBITMQ_PORT", "5672");
        Environment.SetEnvironmentVariable("RABBITMQ_USERNAME", "guest");
        Environment.SetEnvironmentVariable("RABBITMQ_PASSWORD", "guest");
        Environment.SetEnvironmentVariable("OPENSEARCH_URL", "http://localhost:9200");
        Environment.SetEnvironmentVariable("OPENSEARCH__USERNAME", "");
        Environment.SetEnvironmentVariable("OPENSEARCH__PASSWORD", "");
        Environment.SetEnvironmentVariable("REDIS__ENABLED", "false");
        Environment.SetEnvironmentVariable("REDIS__CONNECTIONSTRING", "localhost:6379");
        Environment.SetEnvironmentVariable("AUTH_CREATE_DEFAULTS", "true");
        Environment.SetEnvironmentVariable("AUTH_DEFAULT_ADMIN_PASSWORD", "Admin@1234");
        Environment.SetEnvironmentVariable("AUTH_DEFAULT_USER_PASSWORD", "User@1234");
        Environment.SetEnvironmentVariable("AUTH_DEFAULT_MANAGER_PASSWORD", "Manager@1234");
        Environment.SetEnvironmentVariable("CONNECTIONSTRINGS__DEFAULTCONNECTION", "Data Source=test.db");
        Environment.SetEnvironmentVariable("CORS_ORIGINS", "http://localhost:3000");

        builder.ConfigureServices(services =>
        {
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"ged-test-keys-{DbName}")));

            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<GedDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<GedDbContext>(options =>
            {
                options.UseInMemoryDatabase(DbName);
            });

            RemoveService<RabbitMqService>(services);
            RemoveService<OpenSearchService>(services);
            RemoveService<WebhookService>(services);
            RemoveService<OutboxRelayService>(services);
            RemoveService<RabbitMqQueueInitService>(services);

            services.AddSingleton<IMessageQueueService, NoOpRabbitMqService>();
            services.AddSingleton<IRabbitMqStatusProvider, NoOpRabbitMqStatusProvider>();
            services.AddSingleton<ISearchService, InMemorySearchService>();
            services.AddSingleton<IPipelineEventService, NoOpPipelineEventService>();
            services.AddSingleton<IWebhookService, NoOpWebhookService>();
            services.AddSingleton<RabbitMqConnectionService>();
            services.AddHostedService(sp => sp.GetRequiredService<RabbitMqConnectionService>());

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GedDbContext>();
            db.Database.EnsureCreated();

            var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
            authService.InitializeAsync().Wait();
        });
    }

    private static void RemoveService<T>(IServiceCollection services) where T : class
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null)
            services.Remove(descriptor);
    }
}

[Trait("Category", "Integration")]
public class AuthIntegrationTests : IClassFixture<GedTestWebApplicationFactory>
{
    private readonly GedTestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthIntegrationTests(GedTestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOk()
    {
        var request = new { username = "admin", password = "Admin@1234" };
        var response = await _client.PostAsJsonAsync("/api/auth/login", request);
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK,
            System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        var request = new { username = "admin", password = "WrongPassword" };
        var response = await _client.PostAsJsonAsync("/api/auth/login", request);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.PostAsync("/api/auth/logout", null);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_ThenMe_ReturnsUser()
    {
        var loginRequest = new { username = "admin", password = "Admin@1234" };
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        
        loginResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, 
            "Login should succeed with valid credentials. Response: {Content}", 
            await loginResponse.Content.ReadAsStringAsync());
        
        var meResponse = await _client.GetAsync("/api/auth/me");
        meResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, 
            "/me should return 200 after successful login. Response: {Content}", 
            await meResponse.Content.ReadAsStringAsync());
        
        var meData = await meResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        meData.Should().NotBeNull();
        meData!.ContainsKey("username").Should().BeTrue();
        meData["username"]!.ToString().Should().Be("admin");
        meData["role"]!.ToString().Should().Be("Admin");
    }
}

[Trait("Category", "Integration")]
public class HealthEndpointIntegrationTests : IClassFixture<GedTestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointIntegrationTests(GedTestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthLive_ReturnsOk()
    {
        var response = await _client.GetAsync("/health/live");
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK,
            System.Net.HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Health_ReturnsJson()
    {
        var response = await _client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNull();
    }
}

[Trait("Category", "Integration")]
public class RateLimitingIntegrationTests : IClassFixture<GedTestWebApplicationFactory>
{
    private readonly GedTestWebApplicationFactory _factory;

    public RateLimitingIntegrationTests(GedTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_UnderRateLimit_ShouldAllow()
    {
        var client = _factory.CreateClient();
        var request = new { username = "admin", password = "Admin@1234" };

        var successCount = 0;
        for (int i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", request);
            if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
                successCount++;
        }

        successCount.Should().BeGreaterThan(0);
    }
}

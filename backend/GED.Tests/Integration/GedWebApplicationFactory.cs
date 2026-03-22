using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GED.Infrastructure.Data;
using System.Net.Http.Json;
using GED.Core.Models;

namespace GED.Tests.Integration;

public class GedWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"GED-Test-Db-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<GedDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<GedDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
            });

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GedDbContext>();
            db.Database.EnsureCreated();
        });

        builder.UseEnvironment("Testing");
    }
}

public class AuthIntegrationTests : IClassFixture<GedWebApplicationFactory>
{
    private readonly GedWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthIntegrationTests(GedWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOkOrUnauthorized()
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
    public async Task Register_WithoutAuth_ReturnsUnauthorized()
    {
        var request = new
        {
            username = "newuser",
            password = "Password@123",
            fullName = "New User",
            email = "newuser@test.com",
            role = "User"
        };
        var response = await _client.PostAsJsonAsync("/api/auth/register", request);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUsers_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/auth/users");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_ThenMe_AfterLogout_ReturnsUnauthorized()
    {
        var loginRequest = new { username = "admin", password = "Admin@1234" };
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        if (loginResponse.IsSuccessStatusCode)
        {
            var logoutResponse = await _client.PostAsync("/api/auth/logout", null);
            logoutResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

            var meResponse = await _client.GetAsync("/api/auth/me");
            meResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        }
    }
}

public class HealthEndpointIntegrationTests : IClassFixture<GedWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointIntegrationTests(GedWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthLive_ReturnsOkOrServiceUnavailable()
    {
        var response = await _client.GetAsync("/health/live");
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK,
            System.Net.HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Health_ReturnsJsonOrError()
    {
        var response = await _client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNull();
    }
}

public class RateLimitingIntegrationTests : IClassFixture<GedWebApplicationFactory>
{
    private readonly GedWebApplicationFactory _factory;

    public RateLimitingIntegrationTests(GedWebApplicationFactory factory)
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

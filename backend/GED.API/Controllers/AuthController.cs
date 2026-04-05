using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GED.Core.Models;
using GED.Core.Interfaces;
using GED.API.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace GED.API.Controllers;

/// <summary>
/// Authentication and user management controller.
///
/// Public endpoints:  POST /api/auth/login
/// Admin endpoints:   POST /api/auth/register, GET /api/auth/users, etc.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger      = logger;
    }

    // ── Public ────────────────────────────────────────────────────────────────

[HttpPost("login")]
[AllowAnonymous]
public async Task<IActionResult> Login([FromBody] LoginRequest request)
{
    var clientIp = GetClientIpAddress();
    var result = _authService.Login(request, clientIp);
    if (result == null)
        return Unauthorized(ErrorResponse.Create("Invalid username or password."));

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, result.UserId.ToString()),
        new(ClaimTypes.Name,           result.Username),
        new(ClaimTypes.Role,           result.Role.ToString()),
        new("fullName",                result.FullName ?? result.Username),
        new("sessionToken",            result.Token)
    };
    var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    await HttpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties { ExpiresUtc = result.ExpiresAt });

    // Return user info (no token in the response body anymore)
    return Ok(new { result.Username, result.FullName, result.Role, result.ExpiresAt });
}


[HttpPost("logout")]
[Authorize]
public async Task<IActionResult> Logout()
{
    var sessionToken = User.FindFirst("sessionToken")?.Value;
    if (!string.IsNullOrEmpty(sessionToken))
    {
        _authService.Logout(sessionToken);
        _logger.LogDebug("Session {Token} invalidated on logout", sessionToken[..Math.Min(8, sessionToken.Length)]);
    }
    
    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Ok();
}

    /// <summary>
    /// Returns the current user's profile (from session cookie claims).
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        var role     = User.FindFirst(ClaimTypes.Role)?.Value;
        var fullName = User.FindFirst("fullName")?.Value;

        return Ok(new { username, role, fullName });
    }

    // ── Admin only ────────────────────────────────────────────────────────────

    /// <summary>
    /// Register a new user (Admin only).
    /// </summary>
    [HttpPost("register")]
    [Authorize(Roles = "Admin")]
    public IActionResult Register([FromBody] RegisterRequest request)
    {
        var (success, error) = _authService.Register(request);
        if (!success) return BadRequest(ErrorResponse.Create(error ?? "Registration failed"));

        return Ok(new { message = $"User '{request.Username}' created successfully." });
    }

    /// <summary>
    /// List all users (Admin only).
    /// </summary>
    [HttpGet("users")]
    [Authorize(Roles = "Admin")]
    public ActionResult<List<UserDto>> GetUsers()
    {
        return Ok(_authService.GetAllUsers());
    }

    /// <summary>
    /// Get a user by ID (Admin only).
    /// </summary>
    [HttpGet("users/{id}")]
    [Authorize(Roles = "Admin")]
    public ActionResult<UserDto> GetUser(Guid id)
    {
        var user = _authService.GetUserById(id);
        if (user == null)
            return NotFound(ErrorResponse.Create("User not found", HttpContext.Items["CorrelationId"]?.ToString()));
        return Ok(user);
    }

    /// <summary>
    /// Update a user (Admin only).
    /// </summary>
    [HttpPut("users/{id}")]
    [Authorize(Roles = "Admin")]
    public IActionResult UpdateUser(Guid id, [FromBody] RegisterRequest request)
    {
        var (success, error) = _authService.UpdateUser(id, request);
        if (!success) return BadRequest(ErrorResponse.Create(error ?? "Update failed"));
        return Ok(new { message = "User updated." });
    }

    /// <summary>
    /// Deactivate a user (Admin only).
    /// </summary>
    [HttpDelete("users/{id}")]
    [Authorize(Roles = "Admin")]
    public IActionResult DeactivateUser(Guid id)
    {
        var ok = _authService.DeactivateUser(id);
        if (!ok)
            return NotFound(ErrorResponse.Create("User not found", HttpContext.Items["CorrelationId"]?.ToString()));
        return Ok(new { message = "User deactivated." });
    }

    private string? GetClientIpAddress()
    {
        var forwardedFor = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            var ip = forwardedFor.Split(',').First().Trim();
            if (!string.IsNullOrEmpty(ip))
                return ip;
        }
        
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}

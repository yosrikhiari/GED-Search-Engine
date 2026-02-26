using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GED.Core.Models;
using GED.Infrastructure.Services;
using System.Security.Claims;

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
    private readonly AuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger      = logger;
    }

    // ── Public ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Login with username + password. Returns a JWT token.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Username and password are required." });

        var result = _authService.Login(request);
        if (result == null)
            return Unauthorized(new { error = "Invalid username or password." });

        return Ok(result);
    }

    /// <summary>
    /// Returns the current user's profile (from JWT claims).
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
        if (!success) return BadRequest(new { error });

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
        return user == null ? NotFound() : Ok(user);
    }

    /// <summary>
    /// Update a user (Admin only).
    /// </summary>
    [HttpPut("users/{id}")]
    [Authorize(Roles = "Admin")]
    public IActionResult UpdateUser(Guid id, [FromBody] RegisterRequest request)
    {
        var (success, error) = _authService.UpdateUser(id, request);
        if (!success) return BadRequest(new { error });
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
        return ok ? Ok(new { message = "User deactivated." }) : NotFound();
    }
}

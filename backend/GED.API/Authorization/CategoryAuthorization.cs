using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace GED.API.Authorization;

public class CategoryAccessRequirement : IAuthorizationRequirement
{
    public string CategoryParameterName { get; }
    
    public CategoryAccessRequirement(string categoryParameterName = "category")
    {
        CategoryParameterName = categoryParameterName;
    }
}

public class CategoryAccessHandler : AuthorizationHandler<CategoryAccessRequirement>
{
    private readonly IUserContextProvider _userContextProvider;
    private readonly ILogger<CategoryAccessHandler> _logger;
    private readonly List<string> _availableCategories;

    public CategoryAccessHandler(
        IUserContextProvider userContextProvider,
        ILogger<CategoryAccessHandler> logger)
    {
        _userContextProvider = userContextProvider;
        _logger = logger;
        _availableCategories = GetAvailableCategories();
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CategoryAccessRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext;
        if (httpContext == null)
        {
            context.Fail();
            return Task.CompletedTask;
        }

        var username = context.User.FindFirst(ClaimTypes.Name)?.Value;
        var role = context.User.FindFirst(ClaimTypes.Role)?.Value;

        // Admins bypass category restrictions
        if (role == "Admin")
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (string.IsNullOrEmpty(username))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        // Get category from query, form, or route
        var category = httpContext.Request.Query[requirement.CategoryParameterName].FirstOrDefault()
                    ?? httpContext.Request.Form[requirement.CategoryParameterName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(category))
        {
            // No category specified - let the controller decide
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Normalize category for comparison
        var normalizedCategory = _availableCategories
            .FirstOrDefault(c => c.Equals(category, StringComparison.OrdinalIgnoreCase)) ?? category;

        // Check if category is valid
        if (!_availableCategories.Contains(normalizedCategory, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Invalid category '{Category}' requested by user '{User}'",
                category, username);
            context.Fail();
            return Task.CompletedTask;
        }

        // Check user permissions
        var allowedCategories = _userContextProvider.GetAllowedCategories(username);
        if (allowedCategories == null || allowedCategories.Count == 0)
        {
            // User has no category restrictions - allow access to any category
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (!allowedCategories.Contains(normalizedCategory, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Category ACL violation — user '{User}' attempted to access category '{Category}' (allowed: [{Allowed}])",
                username, category, string.Join(", ", allowedCategories));
            context.Fail();
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }

    private static List<string> GetAvailableCategories()
    {
        return new List<string>
        {
            "Contract", "Invoice", "Report", "Legal", "HR",
            "Financial", "Technical", "Marketing", "Operations",
            "Compliance", "Project", "Customer", "Vendor", "Internal"
        };
    }
}

public interface IUserContextProvider
{
    List<string>? GetAllowedCategories(string username);
}

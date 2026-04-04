using GED.API.Authorization;
using GED.Infrastructure.Services;

namespace GED.API.Services;

public class AuthServiceUserContextProvider : IUserContextProvider
{
    private readonly AuthService _authService;

    public AuthServiceUserContextProvider(AuthService authService)
    {
        _authService = authService;
    }

    public List<string>? GetAllowedCategories(string username)
    {
        return _authService.GetAllowedCategories(username);
    }
}

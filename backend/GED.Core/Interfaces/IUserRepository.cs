using GED.Core.Models;

namespace GED.Core.Interfaces;

/// <summary>
/// Repository interface for user data access.
/// </summary>
public interface IUserRepository
{
    Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<AppUser>> GetAllAsync(CancellationToken ct = default);
    Task<AppUser> AddAsync(AppUser user, CancellationToken ct = default);
    Task UpdateAsync(AppUser user, CancellationToken ct = default);
    Task<bool> ExistsAsync(string username, CancellationToken ct = default);
}

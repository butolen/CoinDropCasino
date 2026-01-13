using CoinDrop;

public interface IAdminUserService
{
    Task<(List<ApplicationUser> Users, int TotalCount)> GetUsersAsync(
        string? searchTerm = null,
        string? sortBy = "CreatedAt",
        bool sortDescending = true,
        int page = 1,
        int pageSize = 20);
    
    Task<string?> GetLastUserActionAsync(int userId);
    Task<bool> ToggleUserSuspensionAsync(int userId);
    Task<bool> ToggleAdminRoleAsync(int userId);
    Task<ApplicationUser?> GetUserDetailsAsync(int userId);
    Task<bool> IsUserAdminAsync(int userId);
    Task<bool> IsUserBannedAsync(int userId);
    Task<bool> UpdateUserBalanceAsync(int userId, double newPhysicalBalance, double newCryptoBalance);
    
    Task<Dictionary<int, (bool IsAdmin, bool IsBanned, string LastAction)>> GetUserStatusBatchAsync(List<int> userIds);
}


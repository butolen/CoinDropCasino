using CoinDrop;
using CoinDrop.services.interfaces;

namespace WebApp.services.implementations;
using Microsoft.AspNetCore.Identity;

public sealed class IdentitySeeder : IIdentitySeeder
{
    private const string AdminRoleName = "admin";
    private const string AdminEmail = "admin@test.local";
    private const string AdminPassword = "Admin123!";

    private readonly RoleManager<IdentityRole<int>> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentitySeeder(
        RoleManager<IdentityRole<int>> roleManager,
        UserManager<ApplicationUser> userManager)
    {
        _roleManager = roleManager;
        _userManager = userManager;
    }

    public async Task SeedAsync()
    {
        await EnsureRoleExistsAsync(AdminRoleName);
        await EnsureAdminUserAsync();
    }

    private async Task EnsureRoleExistsAsync(string roleName)
    {
        if (await _roleManager.RoleExistsAsync(roleName))
            return;

        var result = await _roleManager.CreateAsync(new IdentityRole<int>(roleName));
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
    }

    private async Task EnsureAdminUserAsync()
    {
        var adminUser = await _userManager.FindByEmailAsync(AdminEmail);

        if (adminUser is null)
        {
            adminUser = new ApplicationUser
            {
                UserName = AdminEmail,
                Email = AdminEmail,
                EmailConfirmed = true
            };

            var createResult = await _userManager.CreateAsync(adminUser, AdminPassword);
            if (!createResult.Succeeded)
                throw new InvalidOperationException(string.Join(", ", createResult.Errors.Select(e => e.Description)));
        }

        if (!await _userManager.IsInRoleAsync(adminUser, AdminRoleName))
        {
            var addRoleResult = await _userManager.AddToRoleAsync(adminUser, AdminRoleName);
            if (!addRoleResult.Succeeded)
                throw new InvalidOperationException(string.Join(", ", addRoleResult.Errors.Select(e => e.Description)));
        }
    }
}
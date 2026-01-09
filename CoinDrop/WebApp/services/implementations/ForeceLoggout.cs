using CoinDrop;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace WebApp.services.implementations;

public class ForceLogoutMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ForceLogoutMiddleware> _logger;

    public ForceLogoutMiddleware(RequestDelegate next, ILogger<ForceLogoutMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user != null && context.User.Identity?.IsAuthenticated == true)
        {
            // Prüfe, ob der User kürzlich gezwungen wurde sich auszuloggen
            var forceLogoutToken = await userManager.GetAuthenticationTokenAsync(
                user, "ForceLogout", "LastLogoutToken");
            
            if (!string.IsNullOrEmpty(forceLogoutToken))
            {
                // User wurde gezwungen sich auszuloggen
                _logger.LogInformation("User {UserId} has pending force logout. Signing out...", user.Id);
                
                // User ausloggen
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                
                // Cookies löschen
                context.Response.Cookies.Delete(".AspNetCore.Identity.Application");
                context.Response.Cookies.Delete(".AspNetCore.Antiforgery.*");
                
                // Redirect zum Login mit entsprechender Nachricht
                context.Response.Redirect("/login?msg=force-logout");
                return;
            }
            
            // Prüfe auf gesperrte User
            var isLocked = await userManager.IsLockedOutAsync(user);
            if (isLocked)
            {
                var lockoutEnd = await userManager.GetLockoutEndDateAsync(user);
                if (lockoutEnd > DateTimeOffset.UtcNow)
                {
                    _logger.LogInformation("Banned user {UserId} tried to access. Signing out...", user.Id);
                    await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                    context.Response.Redirect("/login?e=banned");
                    return;
                }
            }
        }

        await _next(context);
    }
}
using System.Security.Claims;
using System.Text;
using CoinDrop;
using CoinDrop.services.interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace WebApp.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // E-MAIL BESTÄTIGUNG
        app.MapGet("/confirm-email", async (
            int userId,
            string token,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ISolanService solanaWalletService) =>
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user == null)
                return Results.BadRequest("Invalid user.");

            var tokenBytes = WebEncoders.Base64UrlDecode(token);
            var normalToken = Encoding.UTF8.GetString(tokenBytes);

            var result = await userManager.ConfirmEmailAsync(user, normalToken);
            if (!result.Succeeded)
                return Results.BadRequest("Email confirmation failed.");
            
            if (string.IsNullOrWhiteSpace(user.DepositAddress))
            {
                user.DepositAddress = solanaWalletService.GetUserDepositAddress(user.Id);
                await userManager.UpdateAsync(user);
            }
            // Nach Bestätigung automatisch einloggen
            await signInManager.SignInAsync(user, isPersistent: false);

            return Results.Redirect("/");
        });

        // EXTERNAL LOGIN START (Google / MS)
        app.MapGet("/external-login", (
            string provider,
            string? returnUrl,
            SignInManager<ApplicationUser> signInManager) =>
        {
            returnUrl ??= "/";

            // Identity kümmert sich um .LoginProvider, Correlation, etc.
            var redirectUrl = $"/external-login-callback?returnUrl={Uri.EscapeDataString(returnUrl)}";
            var props = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);

            return Results.Challenge(props, new[] { provider });
        });

        // EXTERNAL LOGIN CALLBACK
        app.MapGet("/external-login-callback", async (
            string? returnUrl,
            HttpContext ctx,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole<int>> roleManager,
            ISolanService solService) =>
        {
            returnUrl ??= "/";

            var info = await signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                // Wenn du keine /login-Seite hast, lieber /register nehmen
                return Results.Redirect("/login?error=externallogininfo");
            }

            // Falls es schon einen verknüpften Login gibt
            var loginResult = await signInManager.ExternalLoginSignInAsync(
                info.LoginProvider,
                info.ProviderKey,
                isPersistent: false,
                bypassTwoFactor: true);

            if (loginResult.Succeeded)
                return Results.Redirect(returnUrl);

            // Neuer User (Google/MS Erstlogin)
            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            var name = info.Principal.FindFirstValue(ClaimTypes.Name);

            if (string.IsNullOrWhiteSpace(email))
                return Results.Redirect("/login?error=noemail");

            var userName = await GenerateUserNameFromEmailAsync(email, userManager);

            var user = new ApplicationUser
            {
                UserName = userName,
                Email = email,
                EmailConfirmed = true // externe Logins gelten als bestätigt
            };

            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
                return Results.Redirect("/login?error=createuserfailed");

            
            if (string.IsNullOrWhiteSpace(user.DepositAddress))
            {
                user.DepositAddress = solService.GetUserDepositAddress(user.Id);
                await userManager.UpdateAsync(user);
            }
            
            const string defaultRole = "customer";
            if (!await roleManager.RoleExistsAsync(defaultRole))
            {
                await roleManager.CreateAsync(new IdentityRole<int>(defaultRole));
            }

            await userManager.AddToRoleAsync(user, defaultRole);

            var addLoginResult = await userManager.AddLoginAsync(user, info);
            if (!addLoginResult.Succeeded)
                return Results.Redirect("/login?error=addloginfailed");

            await signInManager.SignInAsync(user, isPersistent: false);

            return Results.Redirect(returnUrl);
        });
        app.MapPost("/auth/password-login", async (
            HttpContext ctx,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager) =>
        {
            var form = ctx.Request.Form;

            var userNameOrEmail = form["u"].ToString();
            var password        = form["p"].ToString();
            var rememberRaw     = form["rememberMe"].ToString();

            var rememberMe = bool.TryParse(rememberRaw, out var r) && r;

            if (string.IsNullOrWhiteSpace(userNameOrEmail) ||
                string.IsNullOrWhiteSpace(password))
            {
                return Results.BadRequest("Missing credentials");
            }

            ApplicationUser? user = await userManager.FindByNameAsync(userNameOrEmail);
            if (user == null)
            {
                user = await userManager.FindByEmailAsync(userNameOrEmail);
            }

            if (user == null)
            {
                return Results.BadRequest("Unknown user");
            }

            var result = await signInManager.PasswordSignInAsync(
                user,
                password,
                rememberMe,
                lockoutOnFailure: false);
            Console.WriteLine(result.IsNotAllowed);
            Console.WriteLine(result.RequiresTwoFactor);
            Console.WriteLine(result.IsLockedOut);
            Console.WriteLine(result.IsNotAllowed);
            Console.WriteLine(result.ToString());
            if (!result.Succeeded)
            {
                return Results.BadRequest("Login failed");
            }

            return Results.Redirect("/");
        });
        app.MapGet("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Redirect("/");
        });
        return app;
    }

    private static async Task<string> GenerateUserNameFromEmailAsync(
        string email,
        UserManager<ApplicationUser> userManager)
    {
        var baseName = email.Split('@')[0].ToLowerInvariant();
        var final = baseName;
        var counter = 1;

        while (await userManager.FindByNameAsync(final) != null)
        {
            final = $"{baseName}{counter++}";
        }

        return final;
    }
}
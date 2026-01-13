using System.Net;
using System.Security.Claims;
using System.Text;
using CoinDrop;
using CoinDrop.services.dtos;
using CoinDrop.services.interfaces;
using Domain;
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
            ISolanService solanaWalletService,
            IServiceProvider serviceProvider) =>
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user == null)
                return Results.BadRequest("Invalid user.");

            // Ban-Überprüfung
            var isLocked = await userManager.IsLockedOutAsync(user);
            if (isLocked)
            {
                var lockoutEnd = await userManager.GetLockoutEndDateAsync(user);
                if (lockoutEnd > DateTimeOffset.UtcNow)
                {
                    // ✅ Logging für gescheiterte Email-Bestätigung wegen Ban
                    await LogUserActionAsync(
                        serviceProvider,
                        user.Id,
                        LogActionType.Warning,
                        LogUserType.User,
                        $"Email confirmation blocked - account banned: {user.UserName} (ID: {user.Id})");
                    
                    return Results.Redirect("/login?e=banned");
                }
            }

            var tokenBytes = WebEncoders.Base64UrlDecode(token);
            var normalToken = Encoding.UTF8.GetString(tokenBytes);

            var result = await userManager.ConfirmEmailAsync(user, normalToken);
            if (!result.Succeeded)
            {
                // ✅ Logging für fehlgeschlagene Email-Bestätigung
                await LogUserActionAsync(
                    serviceProvider,
                    user.Id,
                    LogActionType.Error,
                    LogUserType.User,
                    $"Email confirmation failed: {user.UserName} (ID: {user.Id}) - Errors: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                
                return Results.BadRequest("Email confirmation failed.");
            }
            
            if (string.IsNullOrWhiteSpace(user.DepositAddress))
            {
                user.DepositAddress = solanaWalletService.GetUserDepositAddress(user.Id);
                await userManager.UpdateAsync(user);
            }
            
            // Nach Bestätigung automatisch einloggen
            await signInManager.SignInAsync(user, isPersistent: false);

            // ✅ Logging für erfolgreiche Email-Bestätigung
            await LogUserActionAsync(
                serviceProvider,
                user.Id,
                LogActionType.UserAction,
                LogUserType.User,
                $"Email confirmed successfully via confirmation link: {user.UserName} (ID: {user.Id})");

            // ForceLogout Token für neuen User setzen
            var logoutToken = $"{Guid.NewGuid()}_{DateTimeOffset.UtcNow:o}";
            await userManager.SetAuthenticationTokenAsync(user, "ForceLogout", "Token", logoutToken);
            
            // Client-Cookie setzen
            var context = signInManager.Context;
            context.Response.Cookies.Append("ForceLogoutToken", logoutToken, new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });

            return Results.Redirect("/");
        });

        // EXTERNAL LOGIN START (Google / MS)
        app.MapGet("/external-login", (
            string provider,
            string? returnUrl,
            SignInManager<ApplicationUser> signInManager) =>
        {
            returnUrl ??= "/";
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
            ISolanService solService,
            IServiceProvider serviceProvider) =>
        {
            returnUrl ??= "/";

            var info = await signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                // ✅ Logging für fehlgeschlagenen External Login
                await LogUserActionAsync(
                    serviceProvider,
                    null,
                    LogActionType.Error,
                    LogUserType.System,
                    $"External login failed - Could not get external login info");
                
                return Results.Redirect("/login?error=externallogininfo");
            }

            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
            {
                // ✅ Logging für External Login ohne Email
                await LogUserActionAsync(
                    serviceProvider,
                    null,
                    LogActionType.Error,
                    LogUserType.System,
                    $"External login failed - No email claim from provider: {info.LoginProvider}");
                
                return Results.Redirect("/login?error=noemail");
            }

            // Existierenden User finden
            var existingUser = await userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                // Ban-Überprüfung für existierenden User
                var isLocked = await userManager.IsLockedOutAsync(existingUser);
                if (isLocked)
                {
                    var lockoutEnd = await userManager.GetLockoutEndDateAsync(existingUser);
                    if (lockoutEnd > DateTimeOffset.UtcNow)
                    {
                        // ✅ Logging für gescheiterten External Login wegen Ban
                        await LogUserActionAsync(
                            serviceProvider,
                            existingUser.Id,
                            LogActionType.Warning,
                            LogUserType.User,
                            $"External login blocked - account banned: {existingUser.UserName} (ID: {existingUser.Id}, Provider: {info.LoginProvider})");
                        
                        return Results.Redirect("/login?e=banned");
                    }
                }

                // Login mit existierendem User
                var loginResult = await signInManager.ExternalLoginSignInAsync(
                    info.LoginProvider,
                    info.ProviderKey,
                    isPersistent: false,
                    bypassTwoFactor: true);

                if (loginResult.Succeeded)
                {
                    // ✅ Logging für erfolgreichen External Login (existierender User)
                    await LogUserActionAsync(
                        serviceProvider,
                        existingUser.Id,
                        LogActionType.UserAction,
                        LogUserType.User,
                        $"External login successful via {info.LoginProvider}: {existingUser.UserName} (ID: {existingUser.Id}, Email: {existingUser.Email})");

                    // ForceLogout Token setzen
                    var serverToken = await userManager.GetAuthenticationTokenAsync(
                        existingUser, "ForceLogout", "Token");
                    
                    if (string.IsNullOrEmpty(serverToken))
                    {
                        serverToken = $"{Guid.NewGuid()}_{DateTimeOffset.UtcNow:o}";
                        await userManager.SetAuthenticationTokenAsync(
                            existingUser, "ForceLogout", "Token", serverToken);
                    }

                    ctx.Response.Cookies.Append("ForceLogoutToken", serverToken, new CookieOptions
                    {
                        HttpOnly = false,
                        Secure = true,
                        SameSite = SameSiteMode.Strict,
                        Expires = DateTimeOffset.UtcNow.AddDays(30)
                    });
                    
                    return Results.Redirect(returnUrl);
                }
                else
                {
                    // ✅ Logging für gescheiterten External Login
                    await LogUserActionAsync(
                        serviceProvider,
                        existingUser.Id,
                        LogActionType.Warning,
                        LogUserType.User,
                        $"External login failed via {info.LoginProvider}: {existingUser.UserName} (ID: {existingUser.Id})");
                }
            }

            // Neuer User (Google/MS Erstlogin)
            var name = info.Principal.FindFirstValue(ClaimTypes.Name);
            var userName = await GenerateUserNameFromEmailAsync(email, userManager);

            var user = new ApplicationUser
            {
                UserName = userName,
                Email = email,
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                // ✅ Logging für fehlgeschlagene User-Erstellung
                await LogUserActionAsync(
                    serviceProvider,
                    null,
                    LogActionType.Error,
                    LogUserType.System,
                    $"External registration failed - User creation failed for email: {email}, Provider: {info.LoginProvider}, Errors: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
                
                return Results.Redirect("/login?error=createuserfailed");
            }

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
            {
                // ✅ Logging für fehlgeschlagenen Login-Add
                await LogUserActionAsync(
                    serviceProvider,
                    user.Id,
                    LogActionType.Error,
                    LogUserType.User,
                    $"External registration failed - Could not add login for: {user.UserName} (ID: {user.Id}, Provider: {info.LoginProvider})");
                
                return Results.Redirect("/login?error=addloginfailed");
            }

            await signInManager.SignInAsync(user, isPersistent: false);

            // ✅ Logging für neue externe Registrierung
            await LogUserActionAsync(
                serviceProvider,
                user.Id,
                LogActionType.UserAction,
                LogUserType.User,
                $"New user registered via {info.LoginProvider}: {user.UserName} (ID: {user.Id}, Email: {user.Email})");

            // ForceLogout Token für neuen User setzen
            var newToken = $"{Guid.NewGuid()}_{DateTimeOffset.UtcNow:o}";
            await userManager.SetAuthenticationTokenAsync(user, "ForceLogout", "Token", newToken);
            
            ctx.Response.Cookies.Append("ForceLogoutToken", newToken, new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });

            return Results.Redirect(returnUrl);
        });

        // PASSWORD LOGIN MIT BAN-ÜBERPRÜFUNG UND LOGGING
        app.MapPost("/auth/password-login", async (
            HttpContext ctx,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IServiceProvider serviceProvider) =>
        {
            var form = ctx.Request.Form;

            var userNameOrEmail = form["u"].ToString();
            var password        = form["p"].ToString();
            var rememberMe      = bool.TryParse(form["rememberMe"].ToString(), out var r) && r;

            if (string.IsNullOrWhiteSpace(userNameOrEmail) || string.IsNullOrWhiteSpace(password))
            {
                // ✅ Logging für leere Login-Daten
                await LogUserActionAsync(
                    serviceProvider,
                    null,
                    LogActionType.Warning,
                    LogUserType.System,
                    $"Login attempt with empty credentials");
                
                return Results.Redirect("/login?e=1");
            }

            ApplicationUser? user = await userManager.FindByNameAsync(userNameOrEmail)
                                    ?? await userManager.FindByEmailAsync(userNameOrEmail);

            if (user == null)
            {
                // ✅ Logging für nicht existierenden User
                await LogUserActionAsync(
                    serviceProvider,
                    null,
                    LogActionType.Warning,
                    LogUserType.System,
                    $"Login attempt for non-existing user: {userNameOrEmail}");
                
                return Results.Redirect("/login?e=1");
            }

            // Ban-Überprüfung
            var isLocked = await userManager.IsLockedOutAsync(user);
            if (isLocked)
            {
                var lockoutEnd = await userManager.GetLockoutEndDateAsync(user);
                if (lockoutEnd > DateTimeOffset.UtcNow)
                {
                    // ✅ Logging für gescheiterten Login wegen Ban
                    await LogUserActionAsync(
                        serviceProvider,
                        user.Id,
                        LogActionType.Warning,
                        LogUserType.User,
                        $"Login attempt blocked - account banned: {user.UserName} (ID: {user.Id})");
                    
                    return Results.Redirect("/login?e=banned");
                }
            }

            var result = await signInManager.PasswordSignInAsync(
                user, password, rememberMe, lockoutOnFailure: false);

            if (!result.Succeeded)
            {
                // ✅ Logging für gescheiterten Login
                await LogUserActionAsync(
                    serviceProvider,
                    user.Id,
                    LogActionType.Warning,
                    LogUserType.User,
                    $"Failed password login attempt: {user.UserName} (ID: {user.Id})");
                
                return Results.Redirect("/login?e=1");
            }

            // ✅ Logging für erfolgreichen Login
            await LogUserActionAsync(
                serviceProvider,
                user.Id,
                LogActionType.UserAction,
                LogUserType.User,
                $"Password login successful: {user.UserName} (ID: {user.Id}, RememberMe: {rememberMe})");

            // IMMER neuen Token generieren beim Login
            var serverToken = $"{Guid.NewGuid()}_{DateTimeOffset.UtcNow:o}";
            await userManager.SetAuthenticationTokenAsync(
                user, "ForceLogout", "Token", serverToken);
            
            // Security Stamp aktualisieren
            await userManager.UpdateSecurityStampAsync(user);

            // Cookie mit gleichen Einstellungen wie Identity setzen
            var cookieOptions = new CookieOptions
            {
                HttpOnly = false,
                Secure = ctx.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = rememberMe ? DateTimeOffset.UtcNow.AddDays(30) : null
            };
            
            ctx.Response.Cookies.Append("ForceLogoutToken", serverToken, cookieOptions);
            
            Console.WriteLine($"DEBUG - Login successful, token set: {serverToken}");

            return Results.Redirect("/");
        });

        // LOGOUT MIT LOGGING
        app.MapGet("/logout", async (
            HttpContext ctx,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IServiceProvider serviceProvider) =>
        {
            var user = await userManager.GetUserAsync(ctx.User);
            
            // ✅ Logging für Logout-Start
            await LogUserActionAsync(
                serviceProvider,
                user?.Id,
                LogActionType.UserAction,
                user != null ? LogUserType.User : LogUserType.System,
                $"Logout initiated {(user != null ? $"for user: {user.UserName} (ID: {user.Id})" : "for anonymous user")}");

            // Normales Logout
            await signInManager.SignOutAsync();
            
            // Cookies löschen
            ctx.Response.Cookies.Delete(".AspNetCore.Identity.Application");
            ctx.Response.Cookies.Delete("ForceLogoutToken");
            
            // Bei eingeloggtem User: ForceLogout Token entfernen
            if (user != null)
            {
                await userManager.RemoveAuthenticationTokenAsync(user, "ForceLogout", "Token");
                
                // ✅ Logging für erfolgreichen Logout
                await LogUserActionAsync(
                    serviceProvider,
                    user.Id,
                    LogActionType.UserAction,
                    LogUserType.User,
                    $"User logged out successfully: {user.UserName} (ID: {user.Id})");
            }

            return Results.Redirect("/");
        });

        // EMAIL CHANGE CONFIRMATION MIT LOGGING
        app.MapGet("/confirm-email-change", async (
            int userId,
            string email,
            string token,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IServiceProvider serviceProvider) =>
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user == null)
            {
                // ✅ Logging für ungültigen User bei Email-Change
                await LogUserActionAsync(
                    serviceProvider,
                    null,
                    LogActionType.Error,
                    LogUserType.System,
                    $"Email change confirmation failed - User not found: ID: {userId}");
                
                return Results.BadRequest("Invalid user.");
            }

            try
            {
                var emailBytes = WebEncoders.Base64UrlDecode(email);
                var newEmail = Encoding.UTF8.GetString(emailBytes);

                var tokenBytes = WebEncoders.Base64UrlDecode(token);
                var normalToken = Encoding.UTF8.GetString(tokenBytes);

                var oldEmail = user.Email;
                var result = await userManager.ChangeEmailAsync(user, newEmail, normalToken);
                if (!result.Succeeded)
                {
                    // ✅ Logging für fehlgeschlagene Email-Änderung
                    await LogUserActionAsync(
                        serviceProvider,
                        user.Id,
                        LogActionType.Error,
                        LogUserType.User,
                        $"Email change failed: {user.UserName} (ID: {user.Id}) from {oldEmail} to {newEmail} - Errors: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                    
                    return Results.BadRequest("Email change failed.");
                }

                // ✅ Logging für erfolgreiche Email-Änderung
                await LogUserActionAsync(
                    serviceProvider,
                    user.Id,
                    LogActionType.UserAction,
                    LogUserType.User,
                    $"Email changed successfully from {oldEmail} to {newEmail}: {user.UserName} (ID: {user.Id})");

                // optional: neu einloggen, damit Claims/Cookie sauber sind
                await signInManager.RefreshSignInAsync(user);

                return Results.Redirect("/settings?msg=email-updated");
            }
            catch (Exception ex)
            {
                // ✅ Logging für Exception bei Email-Change
                await LogUserActionAsync(
                    serviceProvider,
                    user.Id,
                    LogActionType.Error,
                    LogUserType.User,
                    $"Email change confirmation exception: {user.UserName} (ID: {user.Id}) - {ex.Message}");
                
                return Results.BadRequest("Invalid link.");
            }
        });

        // RESET PASSWORD - FORM (GET)
        app.MapGet("/reset-password", (
            int userId,
            string token) =>
        {
            // simples HTML Formular ohne MVC
            var html = $@"
<!doctype html>
<html>
<head>
  <meta charset='utf-8' />
  <meta name='viewport' content='width=device-width, initial-scale=1' />
  <title>Reset Password</title>
  <style>
    body {{ font-family: Arial; background:#0b0b0b; color:#fff; display:flex; justify-content:center; padding:40px; }}
    .box {{ width:420px; background:#121212; border:1px solid #2a2a2a; border-radius:14px; padding:20px; }}
    input {{ width:100%; padding:10px; margin:8px 0; border-radius:10px; border:1px solid #333; background:#0f0f0f; color:#fff; }}
    button {{ width:100%; padding:10px; border-radius:10px; border:0; background:#6a38ff; color:#fff; font-weight:700; cursor:pointer; }}
    .muted {{ opacity:.7; font-size:12px; margin-top:8px; }}
    .error {{ color:#ff4757; margin:10px 0; }}
  </style>
</head>
<body>
  <div class='box'>
    <h2>Reset Password</h2>
    <form method='post' action='/reset-password'>
      <input type='hidden' name='userId' value='{userId}' />
      <input type='hidden' name='token' value='{WebUtility.HtmlEncode(token)}' />

      <input type='password' name='p1' placeholder='New password (min 7 chars)' required minlength='7' />
      <input type='password' name='p2' placeholder='Repeat new password' required minlength='7' />

      <button type='submit'>Set new password</button>
      <div class='muted'>After success you can log in.</div>
    </form>
  </div>
</body>
</html>";

            return Results.Content(html, "text/html");
        });

        // RESET PASSWORD - SUBMIT (POST) MIT BAN-ÜBERPRÜFUNG UND LOGGING
        app.MapPost("/reset-password", async (
            HttpContext ctx,
            UserManager<ApplicationUser> userManager,
            IServiceProvider serviceProvider) =>
        {
            var form = await ctx.Request.ReadFormAsync();

            var userIdRaw = form["userId"].ToString();
            var tokenEnc  = form["token"].ToString();
            var p1        = form["p1"].ToString();
            var p2        = form["p2"].ToString();

            if (!int.TryParse(userIdRaw, out var userId))
            {
                // ✅ Logging für ungültige User-ID
                await LogUserActionAsync(
                    serviceProvider,
                    null,
                    LogActionType.Error,
                    LogUserType.System,
                    $"Password reset failed - Invalid user ID: {userIdRaw}");
                
                return Results.BadRequest("Invalid request.");
            }

            if (string.IsNullOrWhiteSpace(tokenEnc))
            {
                // ✅ Logging für fehlendes Token
                await LogUserActionAsync(
                    serviceProvider,
                    userId,
                    LogActionType.Error,
                    LogUserType.User,
                    $"Password reset failed - Missing token for user ID: {userId}");
                
                return Results.BadRequest("Invalid request.");
            }

            if (string.IsNullOrWhiteSpace(p1) || p1.Length < 7)
            {
                // ✅ Logging für zu kurzes Passwort
                await LogUserActionAsync(
                    serviceProvider,
                    userId,
                    LogActionType.Warning,
                    LogUserType.User,
                    $"Password reset attempt with too short password for user ID: {userId}");
                
                return Results.BadRequest("Password too short.");
            }

            if (p1 != p2)
            {
                // ✅ Logging für nicht übereinstimmende Passwörter
                await LogUserActionAsync(
                    serviceProvider,
                    userId,
                    LogActionType.Warning,
                    LogUserType.User,
                    $"Password reset attempt with mismatched passwords for user ID: {userId}");
                
                return Results.BadRequest("Passwords do not match.");
            }

            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user == null)
            {
                // ✅ Logging für nicht existierenden User
                await LogUserActionAsync(
                    serviceProvider,
                    userId,
                    LogActionType.Error,
                    LogUserType.System,
                    $"Password reset failed - User not found: ID: {userId}");
                
                return Results.BadRequest("Invalid user.");
            }

            // Ban-Überprüfung
            var isLocked = await userManager.IsLockedOutAsync(user);
            if (isLocked)
            {
                var lockoutEnd = await userManager.GetLockoutEndDateAsync(user);
                if (lockoutEnd > DateTimeOffset.UtcNow)
                {
                    // ✅ Logging für gescheiterten Passwort-Reset wegen Ban
                    await LogUserActionAsync(
                        serviceProvider,
                        user.Id,
                        LogActionType.Warning,
                        LogUserType.User,
                        $"Password reset blocked - account banned: {user.UserName} (ID: {user.Id})");
                    
                    return Results.Redirect("/login?e=banned");
                }
            }

            try
            {
                var tokenBytes = WebEncoders.Base64UrlDecode(tokenEnc);
                var normalToken = Encoding.UTF8.GetString(tokenBytes);

                var result = await userManager.ResetPasswordAsync(user, normalToken, p1);
                if (!result.Succeeded)
                {
                    // ✅ Logging für fehlgeschlagenen Passwort-Reset
                    await LogUserActionAsync(
                        serviceProvider,
                        user.Id,
                        LogActionType.Error,
                        LogUserType.User,
                        $"Password reset failed: {user.UserName} (ID: {user.Id}) - Errors: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                    
                    return Results.BadRequest("Reset failed.");
                }

                // ✅ Logging für erfolgreichen Passwort-Reset
                await LogUserActionAsync(
                    serviceProvider,
                    user.Id,
                    LogActionType.UserAction,
                    LogUserType.User,
                    $"Password reset successful: {user.UserName} (ID: {user.Id})");

                return Results.Redirect("/login?msg=pw-reset");
            }
            catch (Exception ex)
            {
                // ✅ Logging für Exception bei Passwort-Reset
                await LogUserActionAsync(
                    serviceProvider,
                    user.Id,
                    LogActionType.Error,
                    LogUserType.User,
                    $"Password reset exception: {user.UserName} (ID: {user.Id}) - {ex.Message}");
                
                return Results.BadRequest("Invalid link.");
            }
        });

        // GET CURRENT LOGOUT TOKEN FROM SERVER
        app.MapGet("/auth/get-logout-token", async (
            HttpContext ctx,
            UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.GetUserAsync(ctx.User);
            if (user == null) return Results.Ok(new { token = "" });

            var token = await userManager.GetAuthenticationTokenAsync(
                user, "ForceLogout", "Token");
            
            return Results.Ok(new { token = token ?? "" });
        });

        // CHECK IF USER NEEDS TO LOGOUT (FOR POLLING)
        app.MapGet("/auth/check-logout", async (
            HttpContext ctx,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IServiceProvider serviceProvider) =>
        {
            try
            {
                var user = await userManager.GetUserAsync(ctx.User);
                if (user == null) 
                {
                    ctx.Response.Cookies.Delete("ForceLogoutToken");
                    return Results.Ok(new { needsLogout = false });
                }

                // EIGENEN Scope für frischen DB Context
                using var scope = ctx.RequestServices.CreateScope();
                var freshUserManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                
                // User frisch aus DB laden
                var freshUser = await freshUserManager.FindByIdAsync(user.Id.ToString());
                if (freshUser == null)
                {
                    await signInManager.SignOutAsync();
                    ctx.Response.Cookies.Delete("ForceLogoutToken");
                    
                    // ✅ Logging für automatischen Logout (Account nicht gefunden)
                    await LogUserActionAsync(
                        serviceProvider,
                        user.Id,
                        LogActionType.Warning,
                        LogUserType.User,
                        $"Auto-logout triggered - Account not found: ID: {user.Id}");
                    
                    return Results.Ok(new { 
                        needsLogout = true,
                        message = "Account not found"
                    });
                }

                // Ban-Status prüfen
                var isLocked = await freshUserManager.IsLockedOutAsync(freshUser);
                if (isLocked)
                {
                    var lockoutEnd = await freshUserManager.GetLockoutEndDateAsync(freshUser);
                    if (lockoutEnd > DateTimeOffset.UtcNow)
                    {
                        await signInManager.SignOutAsync();
                        ctx.Response.Cookies.Delete("ForceLogoutToken");
                        
                        // ✅ Logging für automatischen Logout (Ban)
                        await LogUserActionAsync(
                            serviceProvider,
                            freshUser.Id,
                            LogActionType.Warning,
                            LogUserType.User,
                            $"Auto-logout triggered - Account banned: {freshUser.UserName} (ID: {freshUser.Id})");
                        
                        return Results.Ok(new { 
                            needsLogout = true,
                            message = "Your account has been suspended"
                        });
                    }
                }

                // ForceLogout Token prüfen
                var serverToken = await freshUserManager.GetAuthenticationTokenAsync(
                    freshUser, "ForceLogout", "Token");
                
                var clientToken = ctx.Request.Cookies["ForceLogoutToken"];
                
                // Wenn kein Server-Token, ist alles OK
                if (string.IsNullOrEmpty(serverToken))
                {
                    ctx.Response.Cookies.Delete("ForceLogoutToken");
                    return Results.Ok(new { needsLogout = false });
                }

                // Wenn Client-Token fehlt oder anders ist -> logout
                if (string.IsNullOrEmpty(clientToken) || clientToken != serverToken)
                {
                    await signInManager.SignOutAsync();
                    ctx.Response.Cookies.Delete(".AspNetCore.Identity.Application");
                    ctx.Response.Cookies.Delete("ForceLogoutToken");
                    
                    // ✅ Logging für erzwungenen Logout (Token mismatch)
                    await LogUserActionAsync(
                        serviceProvider,
                        freshUser.Id,
                        LogActionType.AdminAction,
                        LogUserType.User,
                        $"Force logout triggered - Token mismatch: {freshUser.UserName} (ID: {freshUser.Id}) - ServerToken: {serverToken?.Substring(0, Math.Min(20, serverToken.Length))}..., ClientToken: {clientToken?.Substring(0, Math.Min(20, clientToken.Length))}...");
                    
                    return Results.Ok(new { 
                        needsLogout = true,
                        message = "Your session has been terminated by an administrator."
                    });
                }

                return Results.Ok(new { needsLogout = false });
            }
            catch (Exception ex)
            {
                // ✅ Logging für Exception bei Check-Logout
                await LogUserActionAsync(
                    serviceProvider,
                    null,
                    LogActionType.Error,
                    LogUserType.System,
                    $"Check-logout exception: {ex.Message}");
                
                // Bei Fehler kein Logout erzwingen
                return Results.Ok(new { needsLogout = false });
            }
        });

        // SET CLIENT TOKEN COOKIE (CALL AFTER LOGIN)
        app.MapGet("/auth/set-client-token", async (
            HttpContext ctx,
            UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.GetUserAsync(ctx.User);
            if (user == null) return Results.Unauthorized();

            var token = await userManager.GetAuthenticationTokenAsync(
                user, "ForceLogout", "Token");
            
            if (!string.IsNullOrEmpty(token))
            {
                // Store token for 30 days
                ctx.Response.Cookies.Append("ForceLogoutToken", token, new CookieOptions
                {
                    HttpOnly = false, // JavaScript needs access to it
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Expires = DateTimeOffset.UtcNow.AddDays(30)
                });
            }

            return Results.Ok(new { success = true });
        });

        // FORCE LOGOUT ENDPOINT (FOR JAVASCRIPT) MIT LOGGING
        app.MapPost("/auth/force-logout", async (
            HttpContext ctx,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IServiceProvider serviceProvider) =>
        {
            var user = await userManager.GetUserAsync(ctx.User);
            
            // ✅ Logging für Force-Logout Start
            await LogUserActionAsync(
                serviceProvider,
                user?.Id,
                LogActionType.UserAction,
                user != null ? LogUserType.User : LogUserType.System,
                $"JavaScript force logout initiated {(user != null ? $"for user: {user.UserName} (ID: {user.Id})" : "for anonymous user")}");

            // 1. Sign out
            await signInManager.SignOutAsync();
            
            // 2. Delete cookies
            ctx.Response.Cookies.Delete(".AspNetCore.Identity.Application");
            ctx.Response.Cookies.Delete("ForceLogoutToken");
            
            // 3. For logged in user: remove ForceLogout token
            if (user != null)
            {
                await userManager.RemoveAuthenticationTokenAsync(user, "ForceLogout", "Token");
                await userManager.UpdateSecurityStampAsync(user);
                
                // ✅ Logging für erfolgreichen JavaScript-forced Logout
                await LogUserActionAsync(
                    serviceProvider,
                    user.Id,
                    LogActionType.UserAction,
                    LogUserType.User,
                    $"JavaScript forced logout successful: {user.UserName} (ID: {user.Id})");
            }

            return Results.Ok(new { 
                success = true,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        });

        // POST-LOGIN SETUP (OPTIONAL - CAN BE CALLED AFTER LOGIN)
        app.MapGet("/auth/post-login-setup", async (
            HttpContext ctx,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var user = await userManager.GetUserAsync(ctx.User);
            if (user == null) return Results.Unauthorized();

            // 1. Get or create ForceLogout token from server
            var serverToken = await userManager.GetAuthenticationTokenAsync(
                user, "ForceLogout", "Token");
            
            if (string.IsNullOrEmpty(serverToken))
            {
                // On first login: create token
                serverToken = $"{Guid.NewGuid()}_{DateTimeOffset.UtcNow:o}";
                await userManager.SetAuthenticationTokenAsync(
                    user, "ForceLogout", "Token", serverToken);
            }

            // 2. Set client cookie
            ctx.Response.Cookies.Append("ForceLogoutToken", serverToken, new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });

            // 3. Update security stamp (if needed)
            await userManager.UpdateSecurityStampAsync(user);
            
            return Results.Ok(new { success = true, tokenSet = true });
        });

        app.MapGet("/auth/check-login-status", async (
            HttpContext ctx,
            UserManager<ApplicationUser> userManager) =>
        {
            try
            {
                var user = await userManager.GetUserAsync(ctx.User);
                return Results.Ok(new { 
                    isLoggedIn = user != null,
                    userId = user?.Id
                });
            }
            catch (Exception)
            {
                return Results.Ok(new { isLoggedIn = false });
            }
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

    // Hilfsmethode für Logging in den Endpoints
    private static async Task LogUserActionAsync(
        IServiceProvider serviceProvider,
        int? userId,
        LogActionType actionType,
        LogUserType userType,
        string description)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var logRepository = scope.ServiceProvider.GetRequiredService<IRepository<Log>>();
            
            var logEntry = new Log
            {
                UserId = userId,
                ActionType = actionType,
                UserType = userType,
                Description = description,
                Date = DateTime.UtcNow
            };

            await logRepository.AddAsync(logEntry);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Logging failed in endpoint: {ex.Message}");
        }
    }
}
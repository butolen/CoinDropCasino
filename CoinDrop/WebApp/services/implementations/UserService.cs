using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using CoinDrop.services.dtos;
using CoinDrop.services.interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace CoinDrop.services.implementations;

public class UserService : IUserService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly RoleManager<IdentityRole<int>> _roleManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISolanService _solana;
    private readonly IWebHostEnvironment _env;

    // TODO: später in config auslagern
    private const string SmtpFrom = "mathiasbutolen@gmail.com";
    private const string SmtpHost = "smtp.gmail.com";
    private const int SmtpPort = 587;
    private const string SmtpUser = "mathiasbutolen@gmail.com";
    private const string SmtpPass = "tlzbwhyawsugzqlc";

    // Admin-Mail für Username-Requests (keine DB Felder für pending)
    private const string AdminEmail = "mathiasbutolen@gmail.com";

    public UserService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<IdentityRole<int>> roleManager,
        IHttpContextAccessor httpContextAccessor,
        ISolanService solanaService,
        IWebHostEnvironment env)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _httpContextAccessor = httpContextAccessor;
        _solana = solanaService;
        _env = env;
    }

    // -------------------------
    // Mail helpers
    // -------------------------
    public async Task SendEmailAsync(string to, string subject, string bodyHtml)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(SmtpFrom),
            Subject = subject,
            Body = bodyHtml,
            IsBodyHtml = true
        };

        message.To.Add(to);

        using var smtp = new SmtpClient(SmtpHost, SmtpPort)
        {
            Credentials = new NetworkCredential(SmtpUser, SmtpPass),
            EnableSsl = true
        };

        await smtp.SendMailAsync(message);
    }

    private (string scheme, string host) GetBaseUrl()
    {
        var httpContext = _httpContextAccessor.HttpContext
                          ?? throw new InvalidOperationException("No HttpContext");
        return (httpContext.Request.Scheme, httpContext.Request.Host.Value);
    }

    private static string EncodeToken(string token)
        => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static string DecodeToken(string tokenEncoded)
        => Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(tokenEncoded));

    private static string EncodeString(string value)
        => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(value));

    private static string DecodeString(string valueEncoded)
        => Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(valueEncoded));

    // -------------------------
    // Deposit address helper
    // -------------------------
    private async Task EnsureDepositAddressAsync(ApplicationUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.DepositAddress))
            return;

        user.DepositAddress = _solana.GetUserDepositAddress(user.Id);
        await _userManager.UpdateAsync(user);
    }

    // -------------------------
    // Auth core (dein Stand)
    // -------------------------
    public async Task<IdentityResult> RegisterAsync(RegisterRequest regRequest)
    {
        var existingUserByName = await _userManager.FindByNameAsync(regRequest.UserName);
        var existingUserByEmail = await _userManager.FindByEmailAsync(regRequest.Email);

        if (existingUserByName != null || existingUserByEmail != null)
        {
            return IdentityResult.Failed(new IdentityError
            {
                Description = "Der Benutzername oder die E-Mail-Adresse ist bereits vergeben."
            });
        }

        if (regRequest.Password.Length < 7)
        {
            return IdentityResult.Failed(new IdentityError
            {
                Description = "Das Passwort muss mindestens 7 Zeichen lang sein."
            });
        }

        var user = new ApplicationUser
        {
            UserName = regRequest.UserName,
            Email = regRequest.Email,
            EmailConfirmed = false
        };

        var result = await _userManager.CreateAsync(user, regRequest.Password);
        if (!result.Succeeded)
            return result;

        await EnsureDepositAddressAsync(user);

        const string defaultRole = "customer";
        if (!await _roleManager.RoleExistsAsync(defaultRole))
        {
            await _roleManager.CreateAsync(new IdentityRole<int>(defaultRole));
        }

        await _userManager.AddToRoleAsync(user, defaultRole);

        // Bestätigungs-Token erzeugen
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var tokenEncoded = EncodeToken(token);

        var (scheme, host) = GetBaseUrl();

        // ✅ WICHTIG: dein Endpoint ist /confirm-email
        var confirmUrl = $"{scheme}://{host}/confirm-email?userId={user.Id}&token={tokenEncoded}";

        var subject = "Bestätige dein CoinDrop Konto";
        var body = $@"
<p>Willkommen bei CoinDrop, {HtmlEncoder.Default.Encode(user.UserName)}!</p>
<p>Klicke auf diesen Link, um dein Konto zu bestätigen:</p>
<p><a href=""{HtmlEncoder.Default.Encode(confirmUrl)}"">Konto bestätigen</a></p>";

        await SendEmailAsync(user.Email!, subject, body);

        return result;
    }

    public async Task<SignInResult> LoginAsync(LoginRequest request)
    {
        ApplicationUser? user = await _userManager.FindByNameAsync(request.UserNameOrEmail);
        if (user == null)
        {
            user = await _userManager.FindByEmailAsync(request.UserNameOrEmail);
        }

        if (user == null)
            return SignInResult.Failed;

        var valid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!valid)
            return SignInResult.Failed;

        return SignInResult.Success;
    }

    public async Task LogoutAsync()
    {
        await _signInManager.SignOutAsync();
    }

    public async Task<ApplicationUser?> GetCurrentUserAsync(ClaimsPrincipal principal)
    {
        return await _userManager.GetUserAsync(principal);
    }

    // ============================================================
    // SETTINGS FEATURES
    // ============================================================

    // -------------------------
    // Profile image upload
    // -------------------------
    public async Task<IdentityResult> UploadProfileImageAsync(
        ClaimsPrincipal principal,
        Stream fileStream,
        string contentType,
        long length,
        CancellationToken ct = default)
    {
        var user = await _userManager.GetUserAsync(principal);
        if (user == null)
            return IdentityResult.Failed(new IdentityError { Description = "Not authenticated." });

        const long maxBytes = 2 * 1024 * 1024; // 2MB
        if (length <= 0 || length > maxBytes)
            return IdentityResult.Failed(new IdentityError { Description = "Image too large (max 2MB)." });

        var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowed.Contains(contentType))
            return IdentityResult.Failed(new IdentityError { Description = "Invalid format (jpg/png/webp only)." });

        var uploadsRoot = Path.Combine(_env.WebRootPath, "uploads", "pfp");
        Directory.CreateDirectory(uploadsRoot);

        var ext = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".img"
        };

        var fileName = $"{user.Id}{ext}";
        var absPath = Path.Combine(uploadsRoot, fileName);

        await using (var fs = new FileStream(absPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await fileStream.CopyToAsync(fs, ct);
        }

        user.ProfileImage = $"/uploads/pfp/{fileName}";
        return await _userManager.UpdateAsync(user);
    }

    // -------------------------
    // Resend confirm email (uses /confirm-email endpoint)
    // -------------------------
    public async Task<IdentityResult> ResendEmailConfirmationAsync(ClaimsPrincipal principal)
    {
        var user = await _userManager.GetUserAsync(principal);
        if (user == null)
            return IdentityResult.Failed(new IdentityError { Description = "Not authenticated." });

        if (user.EmailConfirmed)
            return IdentityResult.Failed(new IdentityError { Description = "Email already confirmed." });

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var tokenEncoded = EncodeToken(token);

        var (scheme, host) = GetBaseUrl();
        var confirmUrl = $"{scheme}://{host}/confirm-email?userId={user.Id}&token={tokenEncoded}";

        await SendEmailAsync(user.Email!, "Confirm your CoinDrop email",
            $@"<p>Hi {HtmlEncoder.Default.Encode(user.UserName ?? "")}!</p>
               <p><a href=""{HtmlEncoder.Default.Encode(confirmUrl)}"">Confirm email</a></p>");

        return IdentityResult.Success;
    }

    // -------------------------
    // Change email: request + confirm
    // Confirm handled by /confirm-email-change (Razor page or endpoint)
    // -------------------------
    public async Task<IdentityResult> RequestEmailChangeAsync(ClaimsPrincipal principal, string newEmail)
    {
        var user = await _userManager.GetUserAsync(principal);
        if (user == null)
            return IdentityResult.Failed(new IdentityError { Description = "Not authenticated." });

        newEmail = (newEmail ?? "").Trim();
        if (string.IsNullOrWhiteSpace(newEmail))
            return IdentityResult.Failed(new IdentityError { Description = "Email required." });

        var existing = await _userManager.FindByEmailAsync(newEmail);
        if (existing != null && existing.Id != user.Id)
            return IdentityResult.Failed(new IdentityError { Description = "Email already in use." });

        var token = await _userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        var tokenEncoded = EncodeToken(token);
        var emailEncoded = EncodeString(newEmail);

        var (scheme, host) = GetBaseUrl();
        var url = $"{scheme}://{host}/confirm-email-change?userId={user.Id}&email={emailEncoded}&token={tokenEncoded}";

        await SendEmailAsync(newEmail, "Confirm your new email",
            $@"<p>Click to confirm your new email:</p>
               <p><a href=""{HtmlEncoder.Default.Encode(url)}"">Confirm email change</a></p>");

        return IdentityResult.Success;
    }

    public async Task<IdentityResult> ConfirmEmailChangeAsync(int userId, string newEmail, string token)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return IdentityResult.Failed(new IdentityError { Description = "User not found." });

        return await _userManager.ChangeEmailAsync(user, newEmail, token);
    }

    public async Task<IdentityResult> RequestUserNameChangeAsync(ClaimsPrincipal principal, string newUserName)
    {
        var user = await _userManager.GetUserAsync(principal);
        if (user == null)
            return IdentityResult.Failed(new IdentityError { Description = "Not authenticated." });

        newUserName = (newUserName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(newUserName))
            return IdentityResult.Failed(new IdentityError { Description = "Username required." });

        var existing = await _userManager.FindByNameAsync(newUserName);
        if (existing != null && existing.Id != user.Id)
            return IdentityResult.Failed(new IdentityError { Description = "Username already in use." });

        // ✅ direkt ändern
        var res = await _userManager.SetUserNameAsync(user, newUserName);
        if (!res.Succeeded) return res;

        // Cookie/Claims refreshen (optional aber gut)
        await _signInManager.RefreshSignInAsync(user);

        return IdentityResult.Success;
    }
    
    public async Task<IdentityResult> SendPasswordResetLinkByEmailAsync(string email)
    {
        email = (email ?? "").Trim();
        if (string.IsNullOrWhiteSpace(email))
            return IdentityResult.Failed(new IdentityError { Description = "Email required." });

        var user = await _userManager.FindByEmailAsync(email);

        // ✅ Security: immer generisch antworten (keine User-Existenz leaken)
        // ABER intern nur senden wenn user existiert + confirmed
        if (user == null)
            return IdentityResult.Success;

        // optional: nur wenn confirmed
        // if (!user.EmailConfirmed) return IdentityResult.Success;

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var tokenEncoded = EncodeToken(token);

        var (scheme, host) = GetBaseUrl();
        var url = $"{scheme}://{host}/reset-password?userId={user.Id}&token={tokenEncoded}";

        await SendEmailAsync(user.Email!, "Reset your password",
            $@"<p>Click to reset password:</p>
           <p><a href=""{HtmlEncoder.Default.Encode(url)}"">Reset password</a></p>");

        return IdentityResult.Success;
    }

    // -------------------------
    // Password reset: send link + reset
    // -------------------------
    public async Task<IdentityResult> SendPasswordResetLinkAsync(ClaimsPrincipal principal)
    {
        var user = await _userManager.GetUserAsync(principal);
        if (user == null)
            return IdentityResult.Failed(new IdentityError { Description = "Not authenticated." });

        if (string.IsNullOrWhiteSpace(user.Email))
            return IdentityResult.Failed(new IdentityError { Description = "No email set." });

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var tokenEncoded = EncodeToken(token);

        var (scheme, host) = GetBaseUrl();
        var url = $"{scheme}://{host}/reset-password?userId={user.Id}&token={tokenEncoded}";

        await SendEmailAsync(user.Email!, "Reset your password",
            $@"<p><a href=""{HtmlEncoder.Default.Encode(url)}"">Reset password</a></p>");

        return IdentityResult.Success;
    }

    public async Task<IdentityResult> ResetPasswordAsync(int userId, string token, string newPassword)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return IdentityResult.Failed(new IdentityError { Description = "User not found." });

        return await _userManager.ResetPasswordAsync(user, token, newPassword);
    }
}
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using CoinDrop.services.dtos;
using CoinDrop.services.interfaces;
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
    public UserService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<IdentityRole<int>> roleManager,
        IHttpContextAccessor httpContextAccessor,
        ISolanService solanaService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _httpContextAccessor = httpContextAccessor;
        _solana = solanaService;
    }

    private async Task SendEmailAsync(string to, string subject, string bodyHtml)
    {
        using var message = new MailMessage
        {
            From = new MailAddress("mathiasbutolen@gmail.com"),
            Subject = subject,
            Body = bodyHtml,
            IsBodyHtml = true
        };

        message.To.Add(to);

        using var smtp = new SmtpClient("smtp.gmail.com", 587)
        {
            Credentials = new NetworkCredential("mathiasbutolen@gmail.com", "tlzbwhyawsugzqlc"), // Platzhalter
            EnableSsl = true
        };

        await smtp.SendMailAsync(message);
    }
    private async Task EnsureDepositAddressAsync(ApplicationUser user)
    {
        // Schon vorhanden? Dann fertig.
        if (!string.IsNullOrWhiteSpace(user.DepositAddress))
            return;


        // deterministisch: immer gleiche Adresse für den Index
        user.DepositAddress = _solana.GetUserDepositAddress(user.Id);

        // Speichern
        await _userManager.UpdateAsync(user);
    }
    public async Task<IdentityResult> RegisterAsync(RegisterRequest regRequest)
    {
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
        var tokenEncoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        var httpContext = _httpContextAccessor.HttpContext
                          ?? throw new InvalidOperationException("No HttpContext");

        var request = httpContext.Request;

        // https://deine-domain/confirm-email?userId=123&token=abc...
        var confirmUrl = $"{request.Scheme}://{request.Host}/confirm-email?userId={user.Id}&token={tokenEncoded}";

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
        {
            return SignInResult.Failed;
        }

       

        // Nur Passwort prüfen, NICHT sign-in
        var valid = await _userManager.CheckPasswordAsync(user, request.Password);

        if (!valid)
        {
            return SignInResult.Failed;
        }

        // Nur "Success" zurückmelden – Cookie kommt im HTTP-Endpoint.
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
}
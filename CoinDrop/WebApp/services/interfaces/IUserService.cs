using System.Security.Claims;
using CoinDrop.services.dtos;
using Microsoft.AspNetCore.Identity;


namespace CoinDrop.services.interfaces;

public interface IUserService
{
    Task<IdentityResult> RegisterAsync(RegisterRequest request);
    Task<SignInResult> LoginAsync(LoginRequest request);
    Task LogoutAsync();
    Task<ApplicationUser?> GetCurrentUserAsync(ClaimsPrincipal principal);
    Task<IdentityResult> SendPasswordResetLinkByEmailAsync(string email);
    Task<IdentityResult> UploadProfileImageAsync(
        ClaimsPrincipal principal,
        Stream fileStream,
        string contentType,
        long length,
        CancellationToken ct = default);

    Task<IdentityResult> ResendEmailConfirmationAsync(ClaimsPrincipal principal);

    Task<IdentityResult> RequestEmailChangeAsync(ClaimsPrincipal principal, string newEmail);
    Task<IdentityResult> ConfirmEmailChangeAsync(int userId, string newEmail, string token);

    Task<IdentityResult> RequestUserNameChangeAsync(ClaimsPrincipal principal, string newUserName);

    Task<IdentityResult> SendPasswordResetLinkAsync(ClaimsPrincipal principal);
    Task<IdentityResult> ResetPasswordAsync(int userId, string token, string newPassword);
}
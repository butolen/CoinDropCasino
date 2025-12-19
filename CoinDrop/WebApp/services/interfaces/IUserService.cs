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
}
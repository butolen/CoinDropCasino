using System.ComponentModel.DataAnnotations;

namespace CoinDrop.services.dtos;


public class RegisterRequest
{
    [Required]
    [StringLength(32, MinimumLength = 3)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(7, ErrorMessage = "Password must be at least 7 characters long.")]
    public string Password { get; set; } = string.Empty;
}

public class LoginRequest
{
    public string UserNameOrEmail { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
}
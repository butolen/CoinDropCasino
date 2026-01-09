namespace WebApp.services.dtos;

public class UserProfileDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
    public double NetWorth { get; set; }
    public double BalancePhysical { get; set; }
    public double BalanceCrypto { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsBanned { get; set; }
    public string LastAction { get; set; } = string.Empty;
}
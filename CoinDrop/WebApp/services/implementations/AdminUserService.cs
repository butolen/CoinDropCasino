using CoinDrop;
using CoinDrop.services.interfaces;
using Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;

namespace WebApp.services.implementations;

public class AdminUserService : IAdminUserService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IRepository<Log> _logRepository;
    private readonly IRepository<ApplicationUser> _userRepository;
    private readonly ILogger<AdminUserService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // Email Konfiguration
    private const string SmtpFrom = "mathiasbutolen@gmail.com";
    private const string SmtpHost = "smtp.gmail.com";
    private const int SmtpPort = 587;
    private const string SmtpUser = "mathiasbutolen@gmail.com";
    private const string SmtpPass = "tlzbwhyawsugzqlc";

    public AdminUserService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IRepository<Log> logRepository,
        IRepository<ApplicationUser> userRepository,
        ILogger<AdminUserService> logger,
        IServiceProvider serviceProvider,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logRepository = logRepository;
        _userRepository = userRepository;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    private async Task SendEmailAsync(string to, string subject, string bodyHtml)
    {
        try
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
            _logger.LogInformation("Email sent to {To} with subject '{Subject}'", to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email to {To}", to);
        }
    }

    private async Task ForceLogoutUserAsync(int userId)
    {
        try
        {
            // NEUEN DB Context für diesen Vorgang erstellen
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user == null) return;

            // NEUEN Token generieren
            var newToken = $"{Guid.NewGuid()}_{DateTimeOffset.UtcNow:o}";
            
            // Token setzen
            var setResult = await userManager.SetAuthenticationTokenAsync(
                user, "ForceLogout", "Token", newToken);
            
            if (!setResult.Succeeded)
            {
                _logger.LogWarning("Failed to set token for user {UserId}: {Errors}", 
                    userId, string.Join(", ", setResult.Errors.Select(e => e.Description)));
            }
            
            // Security Stamp aktualisieren
            await userManager.UpdateSecurityStampAsync(user);
            
            _logger.LogInformation("Force logout token updated for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forcing logout for user {UserId}", userId);
        }
    }

    public async Task<(List<ApplicationUser> Users, int TotalCount)> GetUsersAsync(
        string? searchTerm = null,
        string? sortBy = "CreatedAt",
        bool sortDescending = true,
        int page = 1,
        int pageSize = 20)
    {
        try
        {
            // EIGENEN Scope für die Query erstellen
            using var scope = _serviceProvider.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IRepository<ApplicationUser>>();
            
            var query = userRepository.Query();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.ToLower();
                query = query.Where(u =>
                    u.Email!.ToLower().Contains(searchTerm) ||
                    u.UserName!.ToLower().Contains(searchTerm));
            }

            query = sortBy.ToLower() switch
            {
                "email" => sortDescending 
                    ? query.OrderByDescending(u => u.Email)
                    : query.OrderBy(u => u.Email),
                
                "username" => sortDescending 
                    ? query.OrderByDescending(u => u.UserName)
                    : query.OrderBy(u => u.UserName),
                
                "balancephysical" => sortDescending 
                    ? query.OrderByDescending(u => u.BalancePhysical)
                    : query.OrderBy(u => u.BalancePhysical),
                
                "balancecrypto" => sortDescending 
                    ? query.OrderByDescending(u => u.BalanceCrypto)
                    : query.OrderBy(u => u.BalanceCrypto),
                
                _ => sortDescending
                    ? query.OrderByDescending(u => u.CreatedAt)
                    : query.OrderBy(u => u.CreatedAt)
            };

            var totalCount = await query.CountAsync();
            var users = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (users, totalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching users for admin dashboard");
            return (new List<ApplicationUser>(), 0);
        }
    }

    public async Task<string?> GetLastUserActionAsync(int userId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var logRepository = scope.ServiceProvider.GetRequiredService<IRepository<Log>>();
            
            var lastLog = await logRepository.Query()
                .Where(l => l.UserId == userId)
                .OrderByDescending(l => l.Date)
                .FirstOrDefaultAsync();

            return lastLog?.Description ?? "No actions yet";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading last action for user {UserId}", userId);
            return "Error loading action";
        }
    }

    public async Task<bool> ToggleUserSuspensionAsync(int userId)
    {
        try
        {
            // EIGENEN Scope für diesen Vorgang
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user == null) 
            {
                _logger.LogWarning("User {UserId} not found for suspension toggle", userId);
                return false;
            }

            var isLocked = await userManager.IsLockedOutAsync(user);
            string action;
            string emailSubject;
            string emailBody;
            
            if (isLocked)
            {
                // Unban User
                await userManager.SetLockoutEndDateAsync(user, null);
                action = "unbanned";
                emailSubject = "🔓 Your CoinDrop Account Has Been Unbanned";
                emailBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; background: #f8f9fa; padding: 20px; border-radius: 10px;'>
                        <div style='background: linear-gradient(135deg, #0d1b2a 0%, #1b263b 100%); color: white; padding: 20px; border-radius: 10px 10px 0 0; text-align: center;'>
                            <h1 style='margin: 0; color: #ffd700;'>🎰 CoinDrop</h1>
                            <p style='margin: 5px 0 0 0; opacity: 0.8;'>Account Unbanned</p>
                        </div>
                        <div style='background: white; padding: 25px; border-radius: 0 0 10px 10px;'>
                            <h2 style='color: #2ecc71;'>Account Reactivated</h2>
                            <p>Dear <strong>{HtmlEncoder.Default.Encode(user.UserName ?? "User")}</strong>,</p>
                            <p>Your CoinDrop account has been <strong style='color: #2ecc71;'>unbanned</strong> and is now active again.</p>
                            <div style='background: #e8f5e9; border-left: 4px solid #2ecc71; padding: 15px; margin: 20px 0;'>
                                <p style='margin: 0;'>✅ All account restrictions have been lifted</p>
                                <p style='margin: 5px 0 0 0;'>✅ Full access restored to all features</p>
                                <p style='margin: 5px 0 0 0;'>✅ You can now log in and play</p>
                            </div>
                            <p>We're happy to welcome you back to our casino community!</p>
                            <div style='text-align: center; margin: 30px 0;'>
                                <a href='https://your-casino-url.com' style='display: inline-block; background: linear-gradient(135deg, #ffd700 0%, #ff9f1a 100%); color: #0d1b2a; padding: 12px 30px; text-decoration: none; border-radius: 25px; font-weight: bold; font-size: 16px;'>Return to CoinDrop</a>
                            </div>
                            <p style='color: #666; font-size: 14px; border-top: 1px solid #eee; padding-top: 15px; margin-top: 20px;'>
                                Best regards,<br>
                                <strong style='color: #ffd700;'>The CoinDrop Team</strong><br>
                                <span style='font-size: 12px;'>🎲 Your premier casino experience</span>
                            </p>
                        </div>
                    </div>";
            }
            else
            {
                // Ban User
                await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                action = "banned";
                emailSubject = "🔒 Your CoinDrop Account Has Been Suspended";
                emailBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; background: #f8f9fa; padding: 20px; border-radius: 10px;'>
                        <div style='background: linear-gradient(135deg, #0d1b2a 0%, #1b263b 100%); color: white; padding: 20px; border-radius: 10px 10px 0 0; text-align: center;'>
                            <h1 style='margin: 0; color: #ffd700;'>🎰 CoinDrop</h1>
                            <p style='margin: 5px 0 0 0; opacity: 0.8;'>Account Suspension Notice</p>
                        </div>
                        <div style='background: white; padding: 25px; border-radius: 0 0 10px 10px;'>
                            <h2 style='color: #ff4757;'>Account Suspended</h2>
                            <p>Dear <strong>{HtmlEncoder.Default.Encode(user.UserName ?? "User")}</strong>,</p>
                            <p>Your CoinDrop account has been <strong style='color: #ff4757;'>suspended</strong> due to a violation of our Terms of Service.</p>
                            <div style='background: #ffebee; border-left: 4px solid #ff4757; padding: 15px; margin: 20px 0;'>
                                <p style='margin: 0;'>⚠️ Your account access has been restricted</p>
                                <p style='margin: 5px 0 0 0;'>⚠️ All active sessions have been terminated</p>
                                <p style='margin: 5px 0 0 0;'>⚠️ Please review our community guidelines</p>
                            </div>
                            <p>If you believe this suspension is an error, please contact our support team immediately.</p>
                            <div style='text-align: center; margin: 30px 0;'>
                                <a href='mailto:support@your-casino.com' style='display: inline-block; background: linear-gradient(135deg, #ff4757 0%, #dc3545 100%); color: white; padding: 12px 30px; text-decoration: none; border-radius: 25px; font-weight: bold; font-size: 16px;'>Contact Support</a>
                            </div>
                            <p style='color: #666; font-size: 14px; border-top: 1px solid #eee; padding-top: 15px; margin-top: 20px;'>
                                For security reasons,<br>
                                <strong style='color: #ff4757;'>CoinDrop Security Team</strong><br>
                                <span style='font-size: 12px;'>🔒 Protecting our community since 2024</span>
                            </p>
                        </div>
                    </div>";
            }

            // Force logout
            await ForceLogoutUserAsync(userId);

            // Log the action
            await LogActionAsync(userId, LogActionType.AdminAction,
                $"User {user.Email} was {action} by admin");
            
            _logger.LogInformation("User {Email} ({UserId}) was {Action}", user.Email, userId, action);

            // Send email notification
            if (!string.IsNullOrEmpty(user.Email))
            {
                await SendEmailAsync(user.Email, emailSubject, emailBody);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling user suspension for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> ToggleAdminRoleAsync(int userId)
    {
        try
        {
            // EIGENEN Scope für diesen Vorgang
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
            
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user == null) 
            {
                _logger.LogWarning("User {UserId} not found for admin role toggle", userId);
                return false;
            }

            var isAdmin = await userManager.IsInRoleAsync(user, "admin");
            string action;
            string emailSubject;
            string emailBody;
            
            if (isAdmin)
            {
                // Remove admin role
                await userManager.RemoveFromRoleAsync(user, "admin");
                action = "removed from admin role";
                emailSubject = "👤 Admin Role Revoked - CoinDrop";
                emailBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; background: #f8f9fa; padding: 20px; border-radius: 10px;'>
                        <div style='background: linear-gradient(135deg, #0d1b2a 0%, #1b263b 100%); color: white; padding: 20px; border-radius: 10px 10px 0 0; text-align: center;'>
                            <h1 style='margin: 0; color: #ffd700;'>🎰 CoinDrop</h1>
                            <p style='margin: 5px 0 0 0; opacity: 0.8;'>Administrative Role Update</p>
                        </div>
                        <div style='background: white; padding: 25px; border-radius: 0 0 10px 10px;'>
                            <h2 style='color: #3498db;'>Admin Role Revoked</h2>
                            <p>Dear <strong>{HtmlEncoder.Default.Encode(user.UserName ?? "User")}</strong>,</p>
                            <p>Your <strong style='color: #3498db;'>administrator privileges</strong> have been revoked.</p>
                            <div style='background: #e3f2fd; border-left: 4px solid #3498db; padding: 15px; margin: 20px 0;'>
                                <p style='margin: 0;'>ℹ️ You no longer have access to admin features</p>
                                <p style='margin: 5px 0 0 0;'>ℹ️ All admin permissions have been removed</p>
                                <p style='margin: 5px 0 0 0;'>ℹ️ Your account continues as a regular user</p>
                            </div>
                            <p>You can continue to use CoinDrop as a regular customer. All your funds and data remain secure.</p>
                            <p style='color: #666; font-size: 14px; border-top: 1px solid #eee; padding-top: 15px; margin-top: 20px;'>
                                Sincerely,<br>
                                <strong style='color: #3498db;'>CoinDrop Administration</strong><br>
                                <span style='font-size: 12px;'>⚖️ Maintaining platform integrity</span>
                            </p>
                        </div>
                    </div>";
            }
            else
            {
                // Add admin role
                await userManager.AddToRoleAsync(user, "admin");
                action = "promoted to admin";
                emailSubject = "👑 Congratulations! You're Now a CoinDrop Admin";
                emailBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; background: #f8f9fa; padding: 20px; border-radius: 10px;'>
                        <div style='background: linear-gradient(135deg, #0d1b2a 0%, #1b263b 100%); color: white; padding: 20px; border-radius: 10px 10px 0 0; text-align: center;'>
                            <h1 style='margin: 0; color: #ffd700;'>🎰 CoinDrop</h1>
                            <p style='margin: 5px 0 0 0; opacity: 0.8;'>Administrator Promotion</p>
                        </div>
                        <div style='background: white; padding: 25px; border-radius: 0 0 10px 10px;'>
                            <h2 style='color: #ff9f1a;'>🎖️ Welcome to the Admin Team!</h2>
                            <p>Dear <strong>{HtmlEncoder.Default.Encode(user.UserName ?? "User")}</strong>,</p>
                            <p>Congratulations! You have been <strong style='color: #ff9f1a;'>promoted to administrator</strong> on CoinDrop.</p>
                            <div style='background: #fff3e0; border-left: 4px solid #ff9f1a; padding: 15px; margin: 20px 0;'>
                                <p style='margin: 0;'>🎯 You now have access to admin dashboard</p>
                                <p style='margin: 5px 0 0 0;'>🎯 Manage users, transactions, and platform settings</p>
                                <p style='margin: 5px 0 0 0;'>🎯 View detailed analytics and reports</p>
                            </div>
                            <p>As an admin, you play a crucial role in maintaining our platform's security and quality. Please review our admin guidelines carefully.</p>
                            <div style='text-align: center; margin: 30px 0;'>
                                <a href='https://your-casino-url.com/admin' style='display: inline-block; background: linear-gradient(135deg, #ff9f1a 0%, #ff7f00 100%); color: white; padding: 12px 30px; text-decoration: none; border-radius: 25px; font-weight: bold; font-size: 16px;'>Access Admin Dashboard</a>
                            </div>
                            <p style='color: #666; font-size: 14px; border-top: 1px solid #eee; padding-top: 15px; margin-top: 20px;'>
                                Welcome aboard,<br>
                                <strong style='color: #ff9f1a;'>CoinDrop Leadership Team</strong><br>
                                <span style='font-size: 12px;'>👑 Trusted with platform administration</span>
                            </p>
                        </div>
                    </div>";
            }

            // Force logout
            await ForceLogoutUserAsync(userId);

            // Log the action
            await LogActionAsync(userId, LogActionType.AdminAction,
                $"User {user.Email} was {action}");
            
            _logger.LogInformation("User {Email} ({UserId}) was {Action}", user.Email, userId, action);

            // Send email notification
            if (!string.IsNullOrEmpty(user.Email))
            {
                await SendEmailAsync(user.Email, emailSubject, emailBody);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling admin role for user {UserId}", userId);
            return false;
        }
    }

    public async Task<ApplicationUser?> GetUserDetailsAsync(int userId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IRepository<ApplicationUser>>();
            
            return await userRepository.GetByIdAsync(u => u.Id == userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user details for {UserId}", userId);
            return null;
        }
    }
    

    public async Task<bool> IsUserAdminAsync(int userId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user == null) return false;
            
            return await userManager.IsInRoleAsync(user, "admin");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking admin status for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> IsUserBannedAsync(int userId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user == null) return false;
            
            var isLocked = await userManager.IsLockedOutAsync(user);
            if (!isLocked) return false;
            
            var lockoutEnd = await userManager.GetLockoutEndDateAsync(user);
            return lockoutEnd > DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking ban status for user {UserId}", userId);
            return false;
        }
    }

    public async Task<Dictionary<int, (bool IsAdmin, bool IsBanned, string LastAction)>> GetUserStatusBatchAsync(List<int> userIds)
    {
        var result = new Dictionary<int, (bool IsAdmin, bool IsBanned, string LastAction)>();
        
        // Process sequentially to avoid DbContext threading issues
        foreach (var userId in userIds)
        {
            try
            {
                var adminTask = IsUserAdminAsync(userId);
                var bannedTask = IsUserBannedAsync(userId);
                var lastActionTask = GetLastUserActionAsync(userId);

                await Task.WhenAll(adminTask, bannedTask, lastActionTask);
                
                result[userId] = (
                    await adminTask,
                    await bannedTask,
                    await lastActionTask
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting status for user {UserId}", userId);
                result[userId] = (false, false, "Error loading");
            }
        }
        
        return result;
    }
    
    private async Task LogActionAsync(int userId, LogActionType actionType, string description)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var logRepository = scope.ServiceProvider.GetRequiredService<IRepository<Log>>();
            
            var log = new Log
            {
                UserId = userId,
                ActionType = actionType,
                UserType = LogUserType.Admin,
                Description = description,
                Date = DateTime.UtcNow
            };
            
            await logRepository.AddAsync(log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging admin action for user {UserId}", userId);
        }
    }
    public async Task<bool> UpdateUserBalanceAsync(int userId, double newPhysicalBalance, double newCryptoBalance)
{
    try
    {
        using var scope = _serviceProvider.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IRepository<ApplicationUser>>();
    
        var user = await userRepository.GetByIdAsync(u => u.Id == userId);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found for balance update", userId);
            return false;
        }

        // Alte Werte speichern für Log
        var oldPhysical = user.BalancePhysical;
        var oldCrypto = user.BalanceCrypto;

        // Neue Werte setzen
        user.BalancePhysical = newPhysicalBalance;
        user.BalanceCrypto = newCryptoBalance;
    
        await userRepository.UpdateAsync(user);
    
        // Log the action
        await LogActionAsync(userId, LogActionType.AdminAction,
            $"Admin updated balances: Physical={oldPhysical:F2}→{newPhysicalBalance:F2}, Crypto={oldCrypto:F2}→{newCryptoBalance:F2}");

        _logger.LogInformation("Updated balances for user {UserId}: Physical={Old}→{New}, Crypto={OldCrypto}→{NewCrypto}",
            userId, oldPhysical, newPhysicalBalance, oldCrypto, newCryptoBalance);

        // ✅ NUR Security Stamp aktualisieren (kein Force Logout!)
        await UpdateUserSecurityStampAsync(userId);

        return true;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error updating balances for user {UserId}", userId);
        return false;
    }
}

private async Task UpdateUserSecurityStampAsync(int userId)
{
    try
    {
        using var scope = _serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null) return;

        // Nur Security Stamp aktualisieren - erzwingt Token-Refresh
        await userManager.UpdateSecurityStampAsync(user);
        
        _logger.LogInformation("Security stamp updated for user {UserId} (balance changed)", userId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error updating security stamp for user {UserId}", userId);
    }
}
}

using CoinDrop;
using CoinDrop.services.interfaces;
using Domain;
using Microsoft.EntityFrameworkCore;
using WebApp.services.dtos;

namespace WebApp.services.implementations;

public sealed class SystemSettingsService : ISystemSettingsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SystemSettingsService> _logger;
    private readonly Lazy<Task> _initializationTask;
    private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

    public SystemSettingsService(
        IServiceScopeFactory scopeFactory,
        ILogger<SystemSettingsService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        
        // Lazy initialization - no blocking .Wait()
        _initializationTask = new Lazy<Task>(InitializeDefaultSettings);
    }

    private async Task EnsureInitializedAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_initializationTask.IsValueCreated)
                return;
                
            try
            {
                await _initializationTask.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during system settings initialization");
                // Don't rethrow - allow service to continue with default values
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task InitializeDefaultSettings()
    {
        using var scope = _scopeFactory.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<IRepository<SystemSetting>>();
        
        // Default Game Configs
        var defaultSettings = new List<SystemSetting>
        {
            // Blackjack Config
            new() {
                Category = SettingCategory.GameConfig.ToString(),
                SettingKey = "blackjack_min_bet",
                SettingValue = "10",
                DataType = "number",
                Description = "Minimum bet amount for Blackjack in EUR",
                IsActive = true,
                ModifiedBy = 1 // System Admin
            },
            new() {
                Category = SettingCategory.GameConfig.ToString(),
                SettingKey = "blackjack_max_bet",
                SettingValue = "1000",
                DataType = "number",
                Description = "Maximum bet amount for Blackjack in EUR",
                IsActive = true,
                ModifiedBy = 1
            },
            new() {
                Category = SettingCategory.GameConfig.ToString(),
                SettingKey = "blackjack_active",
                SettingValue = "true",
                DataType = "boolean",
                Description = "Is Blackjack game active",
                IsActive = true,
                ModifiedBy = 1
            },

            // Roulette Config
            new() {
                Category = SettingCategory.GameConfig.ToString(),
                SettingKey = "roulette_min_bet",
                SettingValue = "5",
                DataType = "number",
                Description = "Minimum bet amount for Roulette in EUR",
                IsActive = true,
                ModifiedBy = 1
            },
            new() {
                Category = SettingCategory.GameConfig.ToString(),
                SettingKey = "roulette_max_bet",
                SettingValue = "500",
                DataType = "number",
                Description = "Maximum bet amount for Roulette in EUR",
                IsActive = true,
                ModifiedBy = 1
            },
            new() {
                Category = SettingCategory.GameConfig.ToString(),
                SettingKey = "roulette_active",
                SettingValue = "true",
                DataType = "boolean",
                Description = "Is Roulette game active",
                IsActive = true,
                ModifiedBy = 1
            },

            // Crypto Fees (SOLANA only)
            new() {
                Category = SettingCategory.CryptoFees.ToString(),
                SettingKey = "sol_deposit_fee_percent",
                SettingValue = "0.5",
                DataType = "number",
                Description = "SOL deposit fee percentage (in EUR)",
                IsActive = true,
                ModifiedBy = 1
            },
            new() {
                Category = SettingCategory.CryptoFees.ToString(),
                SettingKey = "sol_withdrawal_fee_percent",
                SettingValue = "1.0",
                DataType = "number",
                Description = "SOL withdrawal fee percentage (in EUR)",
                IsActive = true,
                ModifiedBy = 1
            },
            new() {
                Category = SettingCategory.CryptoFees.ToString(),
                SettingKey = "sol_min_fee_eur",
                SettingValue = "0.10",
                DataType = "number",
                Description = "Minimum fee for SOL transactions in EUR",
                IsActive = true,
                ModifiedBy = 1
            },
            new() {
                Category = SettingCategory.CryptoFees.ToString(),
                SettingKey = "sol_max_fee_eur",
                SettingValue = "50",
                DataType = "number",
                Description = "Maximum fee for SOL transactions in EUR",
                IsActive = true,
                ModifiedBy = 1
            },
            new() {
                Category = SettingCategory.CryptoFees.ToString(),
                SettingKey = "sol_active",
                SettingValue = "true",
                DataType = "boolean",
                Description = "Is SOL crypto active",
                IsActive = true,
                ModifiedBy = 1
            },

            // Limits
            new() {
                Category = SettingCategory.Limits.ToString(),
                SettingKey = "min_deposit_eur",
                SettingValue = "10",
                DataType = "number",
                Description = "Minimum deposit amount in EUR",
                IsActive = true,
                ModifiedBy = 1
            },
            new() {
                Category = SettingCategory.Limits.ToString(),
                SettingKey = "min_withdrawal_eur",
                SettingValue = "20",
                DataType = "number",
                Description = "Minimum withdrawal amount in EUR",
                IsActive = true,
                ModifiedBy = 1
            },
            new() {
                Category = SettingCategory.Limits.ToString(),
                SettingKey = "max_daily_withdrawal_eur",
                SettingValue = "5000",
                DataType = "number",
                Description = "Maximum daily withdrawal amount in EUR",
                IsActive = true,
                ModifiedBy = 1
            },
            new() {
                Category = SettingCategory.Limits.ToString(),
                SettingKey = "max_monthly_withdrawal_eur",
                SettingValue = "15000",
                DataType = "number",
                Description = "Maximum monthly withdrawal amount in EUR",
                IsActive = true,
                ModifiedBy = 1
            }
        };

        foreach (var setting in defaultSettings)
        {
            var existing = await GetSettingByKeyAsync(settingsRepo, setting.SettingKey);
            if (existing == null)
            {
                await settingsRepo.AddAsync(setting);
                await LogSettingChangeAsync(1, $"Initialized setting: {setting.SettingKey} = {setting.SettingValue}");
            }
        }
    }

    private async Task<SystemSetting?> GetSettingByKeyAsync(IRepository<SystemSetting> repo, string key)
    {
        return await repo.Query()
            .FirstOrDefaultAsync(s => s.SettingKey == key && s.IsActive);
    }

    private async Task<List<SystemSetting>> GetSettingsByCategoryAsync(IRepository<SystemSetting> repo, string category)
    {
        return await repo.Query()
            .Where(s => s.Category == category && s.IsActive)
            .ToListAsync();
    }

    private async Task<SystemSetting> AddOrUpdateSettingAsync(IRepository<SystemSetting> repo, SystemSetting setting)
    {
        var existing = await GetSettingByKeyAsync(repo, setting.SettingKey);
        
        if (existing != null)
        {
            existing.SettingValue = setting.SettingValue;
            existing.DataType = setting.DataType;
            existing.Description = setting.Description;
            existing.IsActive = setting.IsActive;
            existing.LastModified = DateTime.UtcNow;
            existing.ModifiedBy = setting.ModifiedBy;
            
            await repo.UpdateAsync(existing);
            return existing;
        }
        else
        {
            setting.LastModified = DateTime.UtcNow;
            await repo.AddAsync(setting);
            return setting;
        }
    }

    private async Task<bool> BulkUpdateSettingsAsync(IRepository<SystemSetting> repo, List<SystemSetting> settings)
    {
        foreach (var setting in settings)
        {
            setting.LastModified = DateTime.UtcNow;
        }
        
        await repo.UpdateRange(settings);
        return true;
    }

    private async Task LogSettingChangeAsync(int adminUserId, string description)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var logRepo = scope.ServiceProvider.GetRequiredService<IRepository<Log>>();
            
            var log = new Log
            {
                UserId = adminUserId,
                ActionType = LogActionType.AdminAction,
                UserType = LogUserType.Admin,
                Description = description,
                Date = DateTime.UtcNow
            };
            
            await logRepo.AddAsync(log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging setting change by admin {AdminId}", adminUserId);
        }
    }

    public async Task<List<SystemSettingDto>> GetAllSettingsAsync()
    {
        await EnsureInitializedAsync();
        
        using var scope = _scopeFactory.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<IRepository<SystemSetting>>();
        
        // WICHTIG: Materialisiere die Query komplett IM Scope
        var settings = await settingsRepo.Query()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Category)
            .ThenBy(s => s.SettingKey)
            .ToListAsync(); // ToListAsync() muss IM Scope sein!
        
        // Konvertiere zu DTOs NACH der Materialisierung
        return settings.Select(MapToDto).ToList();
    }

    public async Task<SystemSettingDto?> GetSettingAsync(string key)
    {
        await EnsureInitializedAsync();
        
        using var scope = _scopeFactory.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<IRepository<SystemSetting>>();
        
        var setting = await GetSettingByKeyAsync(settingsRepo, key);
        return setting != null ? MapToDto(setting) : null;
    }

    public async Task<List<SystemSettingDto>> GetSettingsByCategoryAsync(string category)
    {
        await EnsureInitializedAsync();
        
        using var scope = _scopeFactory.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<IRepository<SystemSetting>>();
        
        var settings = await GetSettingsByCategoryAsync(settingsRepo, category);
        return settings.Select(MapToDto).ToList();
    }

    public async Task<bool> UpdateSettingAsync(SystemSettingUpdateDto updateDto)
    {
        await EnsureInitializedAsync();
        
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settingsRepo = scope.ServiceProvider.GetRequiredService<IRepository<SystemSetting>>();
            
            var existing = await GetSettingByKeyAsync(settingsRepo, updateDto.SettingKey);
            if (existing == null)
                return false;

            var oldValue = existing.SettingValue;
            existing.SettingValue = updateDto.SettingValue;
            existing.LastModified = DateTime.UtcNow;
            existing.ModifiedBy = updateDto.ModifiedBy;
            
            await settingsRepo.UpdateAsync(existing);
            
            await LogSettingChangeAsync(updateDto.ModifiedBy, 
                $"Updated setting: {updateDto.SettingKey} from '{oldValue}' to '{updateDto.SettingValue}'");
            
            _logger.LogInformation("Setting {Key} updated by admin {AdminId}", 
                updateDto.SettingKey, updateDto.ModifiedBy);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating setting {Key}", updateDto.SettingKey);
            return false;
        }
    }

    public async Task<bool> BulkUpdateSettingsAsync(BulkSettingsUpdateDto bulkUpdate)
    {
        await EnsureInitializedAsync();
        
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settingsRepo = scope.ServiceProvider.GetRequiredService<IRepository<SystemSetting>>();
            
            var settingsToUpdate = new List<SystemSetting>();
            var changeDescriptions = new List<string>();
            
            foreach (var update in bulkUpdate.Settings)
            {
                var existing = await GetSettingByKeyAsync(settingsRepo, update.SettingKey);
                if (existing != null)
                {
                    changeDescriptions.Add($"{update.SettingKey}: '{existing.SettingValue}' → '{update.SettingValue}'");
                    existing.SettingValue = update.SettingValue;
                    existing.LastModified = DateTime.UtcNow;
                    existing.ModifiedBy = update.ModifiedBy;
                    settingsToUpdate.Add(existing);
                }
            }
            
            if (settingsToUpdate.Any())
            {
                await BulkUpdateSettingsAsync(settingsRepo, settingsToUpdate);
                
                await LogSettingChangeAsync(bulkUpdate.ModifiedBy, 
                    $"Bulk updated settings: {string.Join(", ", changeDescriptions)}");
                
                _logger.LogInformation("Bulk updated {Count} settings by admin {AdminId}", 
                    settingsToUpdate.Count, bulkUpdate.ModifiedBy);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk updating settings");
            return false;
        }
    }

    // Game Configuration Methods
    public async Task<GameConfigDto> GetGameConfigAsync(GameType gameType)
    {
        await EnsureInitializedAsync();
        
        var gameKey = gameType.ToString().ToLower();
        
        var minBet = await GetNumericSettingAsync($"{gameKey}_min_bet", gameType == GameType.Blackjack ? 10 : 5);
        var maxBet = await GetNumericSettingAsync($"{gameKey}_max_bet", gameType == GameType.Blackjack ? 1000 : 500);
        var isActive = await GetBooleanSettingAsync($"{gameKey}_active", true);
        
        return new GameConfigDto
        {
            GameType = gameType,
            MinBet = minBet,
            MaxBet = maxBet,
            IsActive = isActive
        };
    }

    public async Task<bool> UpdateGameConfigAsync(GameConfigDto config, int modifiedBy)
    {
        await EnsureInitializedAsync();
        
        var gameKey = config.GameType.ToString().ToLower();
        
        var updates = new List<SystemSettingUpdateDto>
        {
            new() { SettingKey = $"{gameKey}_min_bet", SettingValue = config.MinBet.ToString("F2"), ModifiedBy = modifiedBy },
            new() { SettingKey = $"{gameKey}_max_bet", SettingValue = config.MaxBet.ToString("F2"), ModifiedBy = modifiedBy },
            new() { SettingKey = $"{gameKey}_active", SettingValue = config.IsActive.ToString().ToLower(), ModifiedBy = modifiedBy }
        };
        
        var bulkUpdate = new BulkSettingsUpdateDto
        {
            Settings = updates,
            ModifiedBy = modifiedBy
        };
        
        var result = await BulkUpdateSettingsAsync(bulkUpdate);
        
        if (result)
        {
            await LogSettingChangeAsync(modifiedBy,
                $"Updated {config.GameType} config: Min={config.MinBet:F2}, Max={config.MaxBet:F2}, Active={config.IsActive}");
        }
        
        return result;
    }

    public async Task<List<GameConfigDto>> GetAllGameConfigsAsync()
    {
        await EnsureInitializedAsync();
        
        var configs = new List<GameConfigDto>();
        
        foreach (GameType gameType in Enum.GetValues(typeof(GameType)))
        {
            var config = await GetGameConfigAsync(gameType);
            configs.Add(config);
        }
        
        return configs;
    }

    // Crypto Fees Methods (SOLANA only for now)
    public async Task<CryptoFeeDto> GetCryptoFeeAsync(CryptoAsset asset = CryptoAsset.SOL)
    {
        await EnsureInitializedAsync();
        
        var assetKey = asset.ToString().ToLower();
        
        var depositFee = await GetNumericSettingAsync($"{assetKey}_deposit_fee_percent", 0.5);
        var withdrawalFee = await GetNumericSettingAsync($"{assetKey}_withdrawal_fee_percent", 1.0);
        var minFee = await GetNumericSettingAsync($"{assetKey}_min_fee_eur", 0.10);
        var maxFee = await GetNumericSettingAsync($"{assetKey}_max_fee_eur", 50);
        var isActive = await GetBooleanSettingAsync($"{assetKey}_active", true);
        
        return new CryptoFeeDto
        {
            Asset = asset,
            Network = NetworkType.Solana,
            DepositFeePercent = depositFee,
            WithdrawalFeePercent = withdrawalFee,
            MinFeeEur = minFee,
            MaxFeeEur = maxFee,
            IsActive = isActive
        };
    }

    public async Task<bool> UpdateCryptoFeeAsync(CryptoFeeDto fee, int modifiedBy)
    {
        await EnsureInitializedAsync();
        
        var assetKey = fee.Asset.ToString().ToLower();
        
        var updates = new List<SystemSettingUpdateDto>
        {
            new() { SettingKey = $"{assetKey}_deposit_fee_percent", SettingValue = fee.DepositFeePercent.ToString("F2"), ModifiedBy = modifiedBy },
            new() { SettingKey = $"{assetKey}_withdrawal_fee_percent", SettingValue = fee.WithdrawalFeePercent.ToString("F2"), ModifiedBy = modifiedBy },
            new() { SettingKey = $"{assetKey}_min_fee_eur", SettingValue = fee.MinFeeEur.ToString("F2"), ModifiedBy = modifiedBy },
            new() { SettingKey = $"{assetKey}_max_fee_eur", SettingValue = fee.MaxFeeEur.ToString("F2"), ModifiedBy = modifiedBy },
            new() { SettingKey = $"{assetKey}_active", SettingValue = fee.IsActive.ToString().ToLower(), ModifiedBy = modifiedBy }
        };
        
        var bulkUpdate = new BulkSettingsUpdateDto
        {
            Settings = updates,
            ModifiedBy = modifiedBy
        };
        
        var result = await BulkUpdateSettingsAsync(bulkUpdate);
        
        if (result)
        {
            await LogSettingChangeAsync(modifiedBy,
                $"Updated {fee.Asset} fees: Deposit={fee.DepositFeePercent:F2}%, Withdrawal={fee.WithdrawalFeePercent:F2}%, Min={fee.MinFeeEur:F2}€, Max={fee.MaxFeeEur:F2}€");
        }
        
        return result;
    }

    public async Task<List<CryptoFeeDto>> GetAllCryptoFeesAsync()
    {
        await EnsureInitializedAsync();
        
        var fees = new List<CryptoFeeDto>();
        
        foreach (CryptoAsset asset in Enum.GetValues(typeof(CryptoAsset)))
        {
            var fee = await GetCryptoFeeAsync(asset);
            fees.Add(fee);
        }
        
        return fees;
    }

    // Limits Methods
    public async Task<LimitsDto> GetLimitsAsync()
    {
        await EnsureInitializedAsync();
        
        return new LimitsDto
        {
            MinDepositEur = await GetNumericSettingAsync("min_deposit_eur", 10),
            MinWithdrawalEur = await GetNumericSettingAsync("min_withdrawal_eur", 20),
            MaxDailyWithdrawalEur = await GetNumericSettingAsync("max_daily_withdrawal_eur", 5000),
            MaxMonthlyWithdrawalEur = await GetNumericSettingAsync("max_monthly_withdrawal_eur", 15000)
        };
    }

    public async Task<bool> UpdateLimitsAsync(LimitsDto limits, int modifiedBy)
    {
        await EnsureInitializedAsync();
        
        var updates = new List<SystemSettingUpdateDto>
        {
            new() { SettingKey = "min_deposit_eur", SettingValue = limits.MinDepositEur.ToString("F2"), ModifiedBy = modifiedBy },
            new() { SettingKey = "min_withdrawal_eur", SettingValue = limits.MinWithdrawalEur.ToString("F2"), ModifiedBy = modifiedBy },
            new() { SettingKey = "max_daily_withdrawal_eur", SettingValue = limits.MaxDailyWithdrawalEur.ToString("F2"), ModifiedBy = modifiedBy },
            new() { SettingKey = "max_monthly_withdrawal_eur", SettingValue = limits.MaxMonthlyWithdrawalEur.ToString("F2"), ModifiedBy = modifiedBy }
        };
        
        var bulkUpdate = new BulkSettingsUpdateDto
        {
            Settings = updates,
            ModifiedBy = modifiedBy
        };
        
        var result = await BulkUpdateSettingsAsync(bulkUpdate);
        
        if (result)
        {
            await LogSettingChangeAsync(modifiedBy,
                $"Updated limits: MinDeposit={limits.MinDepositEur:F2}€, MinWithdrawal={limits.MinWithdrawalEur:F2}€, " +
                $"MaxDaily={limits.MaxDailyWithdrawalEur:F2}€, MaxMonthly={limits.MaxMonthlyWithdrawalEur:F2}€");
        }
        
        return result;
    }

    // Helper Methods with EUR-based fees
    public async Task<double> CalculateDepositFeeAsync(CryptoAsset asset, double amountEur)
    {
        await EnsureInitializedAsync();
        
        var fee = await GetCryptoFeeAsync(asset);
        if (!fee.IsActive) return 0;
        
        var calculatedFee = Math.Round(amountEur * (fee.DepositFeePercent / 100), 2);
        return Math.Max(calculatedFee, fee.MinFeeEur);
    }

    public async Task<double> CalculateWithdrawalFeeAsync(CryptoAsset asset, double amountEur)
    {
        await EnsureInitializedAsync();
        
        var fee = await GetCryptoFeeAsync(asset);
        if (!fee.IsActive) return 0;
        
        var calculatedFee = Math.Round(amountEur * (fee.WithdrawalFeePercent / 100), 2);
        var finalFee = Math.Max(calculatedFee, fee.MinFeeEur);
        
        if (fee.MaxFeeEur > 0)
        {
            finalFee = Math.Min(finalFee, fee.MaxFeeEur);
        }
        
        return finalFee;
    }

    public async Task<bool> IsBetAmountValidAsync(GameType gameType, double amount)
    {
        await EnsureInitializedAsync();
        
        var config = await GetGameConfigAsync(gameType);
        return config.IsActive && amount >= config.MinBet && amount <= config.MaxBet;
    }

    public async Task<bool> IsDepositAmountValidAsync(double amount)
    {
        await EnsureInitializedAsync();
        
        var limits = await GetLimitsAsync();
        return amount >= limits.MinDepositEur;
    }

    public async Task<bool> IsWithdrawalAmountValidAsync(double amount)
    {
        await EnsureInitializedAsync();
        
        var limits = await GetLimitsAsync();
        return amount >= limits.MinWithdrawalEur && 
               amount <= limits.MaxDailyWithdrawalEur;
    }

    public async Task<(bool Valid, string? Error)> ValidateGameBetAsync(GameType gameType, double amount)
    {
        await EnsureInitializedAsync();
        
        var config = await GetGameConfigAsync(gameType);
        
        if (!config.IsActive)
            return (false, $"{gameType} is currently not available");
        
        if (amount < config.MinBet)
            return (false, $"Minimum bet for {gameType} is {config.MinBet:F2}€");
        
        if (amount > config.MaxBet)
            return (false, $"Maximum bet for {gameType} is {config.MaxBet:F2}€");
        
        return (true, null);
    }

    public async Task<(bool Valid, string? Error)> ValidateDepositAmountAsync(double amount)
    {
        await EnsureInitializedAsync();
        
        var limits = await GetLimitsAsync();
        
        if (amount < limits.MinDepositEur)
            return (false, $"Minimum deposit amount is {limits.MinDepositEur:F2}€");
        
        return (true, null);
    }

    public async Task<(bool Valid, string? Error)> ValidateWithdrawalAmountAsync(double amount)
    {
        await EnsureInitializedAsync();
        
        var limits = await GetLimitsAsync();
        
        if (amount < limits.MinWithdrawalEur)
            return (false, $"Minimum withdrawal amount is {limits.MinWithdrawalEur:F2}€");
        
        if (amount > limits.MaxDailyWithdrawalEur)
            return (false, $"Maximum daily withdrawal is {limits.MaxDailyWithdrawalEur:F2}€");
        
        return (true, null);
    }

    // Private Helper Methods
    private async Task<double> GetNumericSettingAsync(string key, double defaultValue)
    {
        var setting = await GetSettingAsync(key);
        if (setting != null && double.TryParse(setting.SettingValue, out var value))
            return value;
        return defaultValue;
    }

    private async Task<bool> GetBooleanSettingAsync(string key, bool defaultValue)
    {
        var setting = await GetSettingAsync(key);
        if (setting != null && bool.TryParse(setting.SettingValue, out var value))
            return value;
        return defaultValue;
    }

    private SystemSettingDto MapToDto(SystemSetting setting)
    {
        return new SystemSettingDto
        {
            SettingId = setting.SettingId,
            Category = setting.Category,
            SettingKey = setting.SettingKey,
            SettingValue = setting.SettingValue,
            DataType = setting.DataType,
            Description = setting.Description,
            IsActive = setting.IsActive,
            LastModified = setting.LastModified,
            ModifiedBy = setting.ModifiedBy
        };
    }
}

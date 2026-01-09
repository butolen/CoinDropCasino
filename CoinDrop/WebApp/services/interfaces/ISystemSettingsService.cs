using WebApp.services.dtos;

namespace CoinDrop.services.interfaces;


public interface ISystemSettingsService
{
    // System Settings
    Task<List<SystemSettingDto>> GetAllSettingsAsync();
    Task<SystemSettingDto?> GetSettingAsync(string key);
    Task<List<SystemSettingDto>> GetSettingsByCategoryAsync(string category);
    Task<bool> UpdateSettingAsync(SystemSettingUpdateDto updateDto);
    Task<bool> BulkUpdateSettingsAsync(BulkSettingsUpdateDto bulkUpdate);
    
    // Game Configuration
    Task<GameConfigDto> GetGameConfigAsync(GameType gameType);
    Task<bool> UpdateGameConfigAsync(GameConfigDto config, int modifiedBy);
    Task<List<GameConfigDto>> GetAllGameConfigsAsync();
    
    // Crypto Fees (nur SOLANA für jetzt)
    Task<CryptoFeeDto> GetCryptoFeeAsync(CryptoAsset asset = CryptoAsset.SOL);
    Task<bool> UpdateCryptoFeeAsync(CryptoFeeDto fee, int modifiedBy);
    Task<List<CryptoFeeDto>> GetAllCryptoFeesAsync();
    
    // Limits
    Task<LimitsDto> GetLimitsAsync();
    Task<bool> UpdateLimitsAsync(LimitsDto limits, int modifiedBy);
    
    // Helper Methods
    Task<double> CalculateDepositFeeAsync(CryptoAsset asset, double amountEur);
    Task<double> CalculateWithdrawalFeeAsync(CryptoAsset asset, double amountEur);
    Task<bool> IsBetAmountValidAsync(GameType gameType, double amount);
    Task<bool> IsDepositAmountValidAsync(double amount);
    Task<bool> IsWithdrawalAmountValidAsync(double amount);
    Task<(bool Valid, string? Error)> ValidateGameBetAsync(GameType gameType, double amount);
    Task<(bool Valid, string? Error)> ValidateDepositAmountAsync(double amount);
    Task<(bool Valid, string? Error)> ValidateWithdrawalAmountAsync(double amount);
}
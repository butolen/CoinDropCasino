using CoinDrop;

namespace WebApp.services.dtos;


public class SystemSettingDto
{
    public int SettingId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public string DataType { get; set; } = "string";
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime LastModified { get; set; }
    public int? ModifiedBy { get; set; }
}

public class GameConfigDto
{
    public GameType GameType { get; set; }
    public double MinBet { get; set; }
    public double MaxBet { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CryptoFeeDto
{
    public CryptoAsset Asset { get; set; } = CryptoAsset.SOL;
    public NetworkType Network { get; set; } = NetworkType.Solana;
    public double DepositFeePercent { get; set; } // Prozentuale Gebühr in EUR
    public double WithdrawalFeePercent { get; set; } // Prozentuale Gebühr in EUR
    public double MinFeeEur { get; set; } // Minimum Gebühr in EUR
    public double MaxFeeEur { get; set; } // Maximum Gebühr in EUR (optional)
    public bool IsActive { get; set; } = true;
}

public class LimitsDto
{
    public double MinDepositEur { get; set; }
    public double MinWithdrawalEur { get; set; }
    public double MaxDailyWithdrawalEur { get; set; }
    public double MaxMonthlyWithdrawalEur { get; set; }
}

public class SystemSettingUpdateDto
{
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public int ModifiedBy { get; set; } // Admin UserId
}

public class BulkSettingsUpdateDto
{
    public List<SystemSettingUpdateDto> Settings { get; set; } = new();
    public int ModifiedBy { get; set; } // Admin UserId
}
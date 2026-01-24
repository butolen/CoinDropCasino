namespace CoinDrop.config;

public class CryptoConfig
{
    public string Cluster { get; set; } = "MainNet";
    public string TreasuryMnemonic { get; set; } = null!;
    public string MasterMnemonic { get; set; } = null!;
    public string TreasuryAddress { get; set; } = null!;
}
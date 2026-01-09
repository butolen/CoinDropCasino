using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CoinDrop;

// ENTFERNE [Table("system_settings")] hier!
public class SystemSetting
{
    [Key]
    [Column("setting_id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int SettingId { get; set; }

    [Column("category", TypeName = "varchar(50)")]
    [Required]
    public string Category { get; set; } = string.Empty;

    [Column("setting_key", TypeName = "varchar(100)")]
    [Required]
    public string SettingKey { get; set; } = string.Empty;

    [Column("setting_value", TypeName = "text")]
    public string SettingValue { get; set; } = string.Empty;

    [Column("data_type", TypeName = "varchar(20)")]
    public string DataType { get; set; } = "string";

    [Column("description", TypeName = "varchar(500)")]
    public string? Description { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("last_modified")]
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    [Column("modified_by")]
    public int? ModifiedBy { get; set; }
}

// Enums für einfachere Verwaltung
public enum SettingCategory
{
    GameConfig,
    CryptoFees,
    Limits,
    General
}



public enum CryptoAsset
{
    SOL, // Nur SOLANA für den Anfang, aber erweiterbar
    // BTC, ETH, USDT, USDC - für später
}

public enum NetworkType
{
    Solana, // Nur Solana für den Anfang
    // Ethereum, Bitcoin - für später
}
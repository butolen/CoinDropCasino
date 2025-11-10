namespace CoinDrop;

using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public enum DepositStatus
{
    Pending,
    Confirmed,
    Swept
}

[Table("crypto_deposit")]
public class CryptoDeposit
{
    [Key]
    [Column("deposit_id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int DepositId { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("network", TypeName = "varchar(30)")]
    [DefaultValue("Solana")]
    public string Network { get; set; } = "Solana";

    [Column("deposit_address", TypeName = "varchar(100)")]
    public string DepositAddress { get; set; } = string.Empty;

    [Column("asset", TypeName = "varchar(50)")]
    [DefaultValue("SOL")]
    public string Asset { get; set; } = "SOL";

    [Column("amount", TypeName = "double")]
    public double Amount { get; set; }

    [Column("txhash", TypeName = "varchar(150)")]
    public string TxHash { get; set; } = string.Empty;

    [Column("confirmations", TypeName = "int")]
    [DefaultValue(1)]
    public int Confirmations { get; set; } = 1;

    [Column("status", TypeName = "varchar(20)")]
    public DepositStatus Status { get; set; } = DepositStatus.Pending;

    [Column("timestamp", TypeName = "datetime")]
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public ApplicationUser User { get; set; } = null!;
    
    /// classifizierung von münzen
    /// xyolo?
    ///opencv?
    /// oda selba cnn trainieren
    /// keggle.com
    
}
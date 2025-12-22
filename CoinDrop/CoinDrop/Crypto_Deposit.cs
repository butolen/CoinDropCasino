using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using CoinDrop;

[Table("crypto_deposit")]
public class CryptoDeposit : Transaction
{
    [Column("network", TypeName = "varchar(30)")]
    [DefaultValue("Solana")]
    public string Network { get; set; } = "Solana";

    [Column("deposit_address", TypeName = "varchar(100)")]
    public string DepositAddress { get; set; } = string.Empty;

    [Column("src_address", TypeName = "varchar(100)")]
    public string SourceAddress { get; set; } = string.Empty;

    [Column("asset", TypeName = "varchar(50)")]
    [DefaultValue("SOL")]
    public string Asset { get; set; } = "SOL";
    
    [Column("amount", TypeName = "double")]
    public double Amount { get; set; }


    [Column("txhash", TypeName = "varchar(150)")]
    public string TxHash { get; set; } = string.Empty;


}
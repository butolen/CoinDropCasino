using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using CoinDrop;

[Table("physical_deposit")]
public class HardwareDeposit : Transaction
{
    [Column("session_code", TypeName = "varchar(20)")]
    public string SessionCode { get; set; } = string.Empty;

    [Column("coin_value", TypeName = "double")]
    public double CoinValue { get; set; }

    [Column("confirmed")]
    [DefaultValue(false)]
    public bool Confirmed { get; set; } = false;
}


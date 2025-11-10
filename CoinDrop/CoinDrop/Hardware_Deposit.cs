namespace CoinDrop;

using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("hardware_deposit")]
public class HardwareDeposit
{
    [Key]
    [Column("session_id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int SessionId { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("session_code", TypeName = "varchar(20)")]
    public string SessionCode { get; set; } = string.Empty;

    [Column("coin_value", TypeName = "double")]
    public double CoinValue { get; set; }

    [Column("timestamp", TypeName = "datetime")]
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Column("confirmed")]
    [DefaultValue(false)]
    public bool Confirmed { get; set; } = false;

    public ApplicationUser User { get; set; } = null!;
}
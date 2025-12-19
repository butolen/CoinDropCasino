namespace CoinDrop;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public enum TransactionType
{
    DepositPhysical,
    DepositCrypto,
    Withdrawal
}
[Table("transaction")]
public class Transaction
{
    [Key]
    [Column("transaction_id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int TransactionId { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("type", TypeName = "varchar(30)")]
    [Required]
    public TransactionType Type { get; set; }

  
    [Column("amount", TypeName = "double")]
    public double Amount { get; set; }


    [Column("timestamp", TypeName = "datetime")]
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Column("details", TypeName = "text")]
    public string? Details { get; set; }

    // Navigation
    public ApplicationUser User { get; set; } = null!;
}
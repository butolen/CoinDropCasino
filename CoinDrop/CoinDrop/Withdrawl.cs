namespace CoinDrop;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public enum WithdrawalStatus
{
    Pending,
    Approved,
    Rejected,
    Sent
}

[Table("withdrawal_request")]
public class Withdrawal : Transaction
{
    [Column("target_address", TypeName = "varchar(100)")]
    public string TargetAddress { get; set; } = string.Empty;

    [Column("asset", TypeName = "varchar(50)")]
    public string Asset { get; set; } = "SOL";

    
    [Column("amount", TypeName = "double")]
    public double Amount { get; set; }

    

    [Column("status", TypeName = "varchar(20)")]
    public WithdrawalStatus Status { get; set; }

    [Column("txhash", TypeName = "varchar(150)")]
    public string? TxHash { get; set; }
}
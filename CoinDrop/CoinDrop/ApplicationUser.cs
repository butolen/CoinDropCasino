using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace CoinDrop;

[Table("user")]
public class ApplicationUser : IdentityUser<int>
{
    [Column("user_id")]
    public override int Id { get; set; }  // PK-Spalte in der DB heißt user_id

    [Column("balancephysical", TypeName = "double")]
    [DefaultValue(0.0)]
    public double BalancePhysical { get; set; } = 0.0;

    [Column("balancecrypto", TypeName = "double")]
    [DefaultValue(0.0)]
    public double BalanceCrypto { get; set; } = 0.0;

    [Column("profileimage", TypeName = "varchar(255)")]
    public string? ProfileImage { get; set; }
/*
    [Column("issuspended")] -- ist in if vorhandne 
    [DefaultValue(false)]
    public bool IsSuspended { get; set; } = false;
*/  
    [Column("depositaddress", TypeName = "varchar(100)")]
    public string? DepositAddress { get; set; }

    [Column("createdat", TypeName = "datetime")]
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public double TotalBalance => BalancePhysical + BalanceCrypto;

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<GameSession> GameSessions { get; set; } = new List<GameSession>();

}
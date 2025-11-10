namespace CoinDrop;

using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public enum GameType
{
    Blackjack,
    Roulette
}

public enum GameResult
{
    Win,
    Loss,
    Draw
}

[Table("game_session")]
public class GameSession
{
    [Key]
    [Column("session_id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int SessionId { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("game_type", TypeName = "varchar(20)")]
    public GameType GameType { get; set; }

    [Column("bet_amount", TypeName = "double")]
    public double BetAmount { get; set; }

    [Column("result", TypeName = "varchar(10)")]
    public GameResult Result { get; set; }

    [Column("win_amount", TypeName = "double")]
    [DefaultValue(0.0)]
    public double WinAmount { get; set; } = 0.0;

    [Column("balance_before", TypeName = "double")]
    public double BalanceBefore { get; set; }

    [Column("balance_after", TypeName = "double")]
    public double BalanceAfter { get; set; }

    [Column("timestamp", TypeName = "datetime")]
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public ApplicationUser User { get; set; } = null!;
}
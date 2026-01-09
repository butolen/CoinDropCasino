namespace WebApp.services.dtos;

public class GameHistoryRowDto
{
    public string Game { get; set; } = string.Empty; // "Blackjack" oder "Roulette"
    public double BetAmount { get; set; }
    public string Result { get; set; } = string.Empty; // "Win", "Loss", "Draw"
    public double WinAmount { get; set; }
    public DateTime TimestampUtc { get; set; }
    public double BalanceBefore { get; set; }
    public double BalanceAfter { get; set; }
}

public enum GameTypeFilter
{
    All,
    Blackjack,
    Roulette
}

public enum GameResultFilter
{
    All,
    Win,
    Loss,
    Draw
}

public class GameHistoryQuery
{
    public GameTypeFilter GameType { get; set; } = GameTypeFilter.All;
    public GameResultFilter Result { get; set; } = GameResultFilter.All;
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public double? MinBetAmount { get; set; }
    public double? MinWinAmount { get; set; }
}
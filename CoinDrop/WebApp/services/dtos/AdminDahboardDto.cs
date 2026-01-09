namespace WebApp.services.dtos;

public enum TimeRange
{
    Today,
    LastWeek,
    LastMonth,
    LastYear
}

public sealed class DashboardStatsDto
{
    public double CasinoNetFromGames { get; set; }              // Gewinne - Verluste (Casino Sicht) [im Zeitraum]

    public int CasinoGamesWon { get; set; }                     // User Loss (Casino gewinnt) [im Zeitraum]
    public int CasinoGamesLost { get; set; }                    // User Win  (Casino verliert) [im Zeitraum]
    public int GamesDraw { get; set; }                          // [im Zeitraum]
    public double WinOrLose { get; set; } //in range
    public double DepositsMinusWithdrawals { get; set; }         // [im Zeitraum]
    public double WithdrawalsSum { get; set; }                   // [im Zeitraum]
    public double CryptoDepositsSum { get; set; }                // [im Zeitraum]
    public double HardwareDepositsSum { get; set; }              // [im Zeitraum]

    public double HighestBet { get; set; }                       // [im Zeitraum]

    public double RangeTotalFormula { get; set; }                // (Einzahlungen + Gewinne - Auszahlungen + Verluste) [im Zeitraum]
}
public sealed class PagedResultAdmin<T>
{
    public List<T> Items { get; set; } = new();
    public bool HasMore { get; set; }
}

public sealed class LogItemDto
{
    public int LogId { get; set; }
    public DateTime Date { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string UserType { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public string Description { get; set; } = string.Empty;
}

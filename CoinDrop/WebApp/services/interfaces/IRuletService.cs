using CoinDrop;

public interface IRouletteService
{
    enum BetType
    {
        StraightUp, Red, Black, Even, Odd, Low, High,
        Dozen1, Dozen2, Dozen3, Column1, Column2, Column3
    }

    class RouletteBet
    {
        public BetType Type { get; set; }
        public int? Number { get; set; }
        public double Amount { get; set; }
        public double Odds => GetOddsForBetType(Type);
        
        private static double GetOddsForBetType(BetType type)
        {
            return type switch
            {
                BetType.StraightUp => 35,
                BetType.Dozen1 or BetType.Dozen2 or BetType.Dozen3 => 2,
                BetType.Column1 or BetType.Column2 or BetType.Column3 => 2,
                _ => 1 // Red, Black, Even, Odd, Low, High
            };
        }
    }

    class RouletteResult
    {
        public int WinningNumber { get; set; }
        public string Color { get; set; } = string.Empty;
        public bool IsEven => WinningNumber % 2 == 0 && WinningNumber != 0;
        public bool IsLow => WinningNumber >= 1 && WinningNumber <= 18;
        public bool IsHigh => WinningNumber >= 19 && WinningNumber <= 36;
        public int Dozen => WinningNumber == 0 ? 0 : (WinningNumber - 1) / 12 + 1;
        public int Column => WinningNumber == 0 ? 0 : (WinningNumber % 3 == 0 ? 3 : WinningNumber % 3);
        public List<RouletteBet> WinningBets { get; set; } = new();
        public double TotalWin { get; set; }
        public double TotalBet { get; set; }
        public double NetWin => TotalWin - TotalBet;
        public double NewBalance { get; set; }
        public string ResultMessage { get; set; } = string.Empty;
    }

    Task<(double minBet, double maxBet, bool isActive, string error)> GetBetLimitsAsync();
    Task<(RouletteResult? result, string error)> PlaceBetsAsync(int userId, List<RouletteBet> bets);
    Task<double> GetUserBalanceAsync(int userId);
    Task<List<GameSession>> GetUserGameHistoryAsync(int userId, int skip = 0, int take = 10);
    Task<(double totalBet, double totalWin, int gamesPlayed)> GetUserStatsAsync(int userId);
    Dictionary<BetType, string> GetBetTypeDisplayNames();
    List<int> GetRedNumbers();
    List<int> GetBlackNumbers();
}
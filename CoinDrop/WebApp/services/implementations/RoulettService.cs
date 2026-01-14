using CoinDrop;
using CoinDrop.services.interfaces;
using Domain;
using Microsoft.EntityFrameworkCore;

public class RouletteService : IRouletteService
{
    private readonly IRepository<GameSession> _gameSessionRepo;
    private readonly IRepository<SystemSetting> _settingsRepo;
    private readonly IRepository<ApplicationUser> _userRepo;
    private readonly IUserService _userService;
    private readonly ILogger<RouletteService> _logger;

    private const int MIN_NUMBER = 0;
    private const int MAX_NUMBER = 36;
    
    private static readonly int[] RED_NUMBERS = { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };
    private static readonly int[] BLACK_NUMBERS = { 2, 4, 6, 8, 10, 11, 13, 15, 17, 20, 22, 24, 26, 28, 29, 31, 33, 35 };

    public RouletteService(
        IRepository<GameSession> gameSessionRepo,
        IRepository<SystemSetting> settingsRepo,
        IRepository<ApplicationUser> userRepo,
        IUserService userService,
        ILogger<RouletteService> logger)
    {
        _gameSessionRepo = gameSessionRepo;
        _settingsRepo = settingsRepo;
        _userRepo = userRepo;
        _userService = userService;
        _logger = logger;
    }

    public async Task<(double minBet, double maxBet, bool isActive, string error)> GetBetLimitsAsync()
    {
        try
        {
            var minBetSetting = await _settingsRepo.GetByIdAsync(
                s => s.Category == "GameConfig" && s.SettingKey == "roulette_min_bet");
            
            var maxBetSetting = await _settingsRepo.GetByIdAsync(
                s => s.Category == "GameConfig" && s.SettingKey == "roulette_max_bet");
            
            var activeSetting = await _settingsRepo.GetByIdAsync(
                s => s.Category == "GameConfig" && s.SettingKey == "roulette_active");

            double minBet = minBetSetting != null && double.TryParse(minBetSetting.SettingValue, out var min) ? min : 5.0;
            double maxBet = maxBetSetting != null && double.TryParse(maxBetSetting.SettingValue, out var max) ? max : 500.0;
            bool isActive = activeSetting?.SettingValue?.ToLower() == "true";

            return (minBet, maxBet, isActive, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving roulette limits");
            return (0, 0, false, "Error loading game limits");
        }
    }

    public async Task<(IRouletteService.RouletteResult? result, string error)> PlaceBetsAsync(int userId, List<IRouletteService.RouletteBet> bets)
    {
        try
        {
            var (minBet, maxBet, isActive, limitsError) = await GetBetLimitsAsync();
            if (!string.IsNullOrEmpty(limitsError))
                return (null, limitsError);
            
            if (!isActive)
                return (null, "Roulette is currently not active");

            var user = await _userRepo.GetByIdAsync(u => u.Id == userId);
            if (user == null)
                return (null, "User not found");

            // Gesamteinsatz berechnen
            double totalBet = bets.Sum(b => b.Amount);
            
            if (totalBet < minBet)
                return (null, $"Minimum total bet is {minBet}€");
            if (totalBet > maxBet)
                return (null, $"Maximum total bet is {maxBet}€");
            
            // Prüfe GESAMTBALANCE (Crypto + Physical)
            double totalBalance = user.BalanceCrypto + user.BalancePhysical;
            if (totalBalance < totalBet)
                return (null, "Insufficient total balance");

            // Logging der platzierten Wetten
            var betDetails = string.Join(", ", bets.Select(b => 
                $"{b.Type}{(b.Number.HasValue ? $"({b.Number})" : "")}: {b.Amount}€"));
            
            await _userService.LogUserActionAsync(
                userId,
                LogActionType.UserAction,
                LogUserType.User,
                $"Roulette bets placed - Total: {totalBet}€, Details: {betDetails}");

            // Gewinnzahl generieren
            var random = new Random();
            int winningNumber = random.Next(MIN_NUMBER, MAX_NUMBER + 1);
            
            // Ergebnis analysieren
            var result = AnalyzeResult(winningNumber);
            
            // Wetten auswerten und Gewinne berechnen
            double totalWin = 0;
            var winningBets = new List<IRouletteService.RouletteBet>();

            foreach (var bet in bets)
            {
                if (IsWinningBet(bet, winningNumber, result))
                {
                    double winAmount = bet.Amount * (1 + bet.Odds);
                    totalWin += winAmount;
                    winningBets.Add(bet);
                }
            }

            // Alte Balances für Logging speichern
            double oldCrypto = user.BalanceCrypto;
            double oldPhysical = user.BalancePhysical;
            double oldTotal = user.TotalBalance;
            
            // Netto-Änderung berechnen
            double netChange = totalWin - totalBet; // Kann negativ sein (Verlust)
            
            // Balances entsprechend anpassen
            if (netChange >= 0)
            {
                // Gewinn: IMMER zu Crypto hinzufügen
                user.BalanceCrypto += netChange;
            }
            else
            {
                // Verlust: Zuerst von Crypto abziehen, dann von Physical
                double loss = -netChange; // Positive Zahl (Betrag)
                
                // Zuerst von Crypto abziehen
                double cryptoUsed = Math.Min(loss, user.BalanceCrypto);
                user.BalanceCrypto -= cryptoUsed;
                
                // Rest von Physical abziehen
                if (loss > cryptoUsed)
                {
                    double physicalUsed = loss - cryptoUsed;
                    user.BalancePhysical -= physicalUsed;
                }
            }

            await _userRepo.UpdateAsync(user);

            // GameSession erstellen
            var gameSession = new GameSession
            {
                UserId = userId,
                GameType = GameType.Roulette,
                BetAmount = totalBet,
                Result = totalWin > totalBet ? GameResult.Win : (totalWin < totalBet ? GameResult.Loss : GameResult.Draw),
                WinAmount = totalWin,
                BalanceBefore = oldTotal,
                BalanceAfter = user.TotalBalance,
                Timestamp = DateTime.UtcNow
            };

            await _gameSessionRepo.AddAsync(gameSession);

            // Ergebnis vorbereiten (NUR MIT INTERFACE-PROPERTIES!)
            var resultDto = new IRouletteService.RouletteResult
            {
                WinningNumber = result.WinningNumber,
                Color = result.Color,
                WinningBets = winningBets,
                TotalWin = totalWin,
                TotalBet = totalBet,
                NewBalance = user.TotalBalance, // Interface Property
                ResultMessage = GetResultMessage(winningNumber, totalWin, totalBet, result.Color, winningBets)
            };

            // Logging des Ergebnisses
            string balanceChanges = "";
            if (netChange >= 0)
            {
                balanceChanges = $"Gewinn: +{netChange:0.00}€ zu Crypto";
            }
            else
            {
                double cryptoBefore = oldCrypto;
                double physicalBefore = oldPhysical;
                double cryptoAfter = user.BalanceCrypto;
                double physicalAfter = user.BalancePhysical;
                
                balanceChanges = $"Verlust: Crypto {cryptoBefore:0.00}→{cryptoAfter:0.00}€, " +
                               $"Physical {physicalBefore:0.00}→{physicalAfter:0.00}€";
            }

            await _userService.LogUserActionAsync(
                userId,
                LogActionType.Info,
                LogUserType.User,
                $"Roulette result: Number {winningNumber} ({result.Color}) - " +
                $"Total bet: {totalBet}€ - Total win: {totalWin}€ - " +
                $"Net: {netChange:+0.00;-0.00;0}€ - {balanceChanges} - " +
                $"Total Balance: {user.TotalBalance}€");

            return (resultDto, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PlaceBetsAsync");
            return (null, "An error occurred while processing your bet");
        }
    }

    private bool IsWinningBet(IRouletteService.RouletteBet bet, int winningNumber, IRouletteService.RouletteResult result)
    {
        if (winningNumber == 0)
            return bet.Type == IRouletteService.BetType.StraightUp && bet.Number == 0;

        // Verwende die Properties aus dem Interface
        return bet.Type switch
        {
            IRouletteService.BetType.StraightUp => bet.Number == winningNumber,
            IRouletteService.BetType.Red => result.Color == "red",
            IRouletteService.BetType.Black => result.Color == "black",
            IRouletteService.BetType.Even => result.IsEven,
            IRouletteService.BetType.Odd => !result.IsEven,
            IRouletteService.BetType.Low => result.IsLow,
            IRouletteService.BetType.High => result.IsHigh,
            IRouletteService.BetType.Dozen1 => result.Dozen == 1,
            IRouletteService.BetType.Dozen2 => result.Dozen == 2,
            IRouletteService.BetType.Dozen3 => result.Dozen == 3,
            IRouletteService.BetType.Column1 => result.Column == 1,
            IRouletteService.BetType.Column2 => result.Column == 2,
            IRouletteService.BetType.Column3 => result.Column == 3,
            _ => false
        };
    }

    // ✅ Verwendet NUR die Properties aus dem Interface
    private IRouletteService.RouletteResult AnalyzeResult(int number)
    {
        return new IRouletteService.RouletteResult
        {
            WinningNumber = number,
            Color = number == 0 ? "green" : (RED_NUMBERS.Contains(number) ? "red" : "black"),
            // IsEven, IsLow, IsHigh, Dozen, Column werden automatisch berechnet (siehe Interface)
        };
    }

    private string GetResultMessage(int winningNumber, double totalWin, double totalBet, string color, List<IRouletteService.RouletteBet> winningBets)
    {
        double netWin = totalWin - totalBet;
        
        if (winningBets.Any())
        {
            if (netWin > 0)
                return $"🎉 Congratulations! You won {totalWin:0.00}€ (Net: +{netWin:0.00}€) on number {winningNumber} ({color})!";
            else if (netWin < 0)
                return $"You won {totalWin:0.00}€ on number {winningNumber} ({color}) but lost {Math.Abs(netWin):0.00}€ net.";
            else
                return $"You broke even on number {winningNumber} ({color}) - Total win: {totalWin:0.00}€";
        }
        
        return $"Number {winningNumber} ({color}) - No winning bets. Loss: {totalBet:0.00}€";
    }

    public async Task<double> GetUserBalanceAsync(int userId)
    {
        var user = await _userRepo.GetByIdAsync(u => u.Id == userId);
        return user?.TotalBalance ?? 0;
    }

    public async Task<List<GameSession>> GetUserGameHistoryAsync(int userId, int skip = 0, int take = 10)
    {
        return await _gameSessionRepo.Query()
            .Where(gs => gs.UserId == userId && gs.GameType == GameType.Roulette)
            .OrderByDescending(gs => gs.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<(double totalBet, double totalWin, int gamesPlayed)> GetUserStatsAsync(int userId)
    {
        var sessions = await _gameSessionRepo.Query()
            .Where(gs => gs.UserId == userId && gs.GameType == GameType.Roulette)
            .ToListAsync();

        return (
            sessions.Sum(gs => gs.BetAmount),
            sessions.Sum(gs => gs.WinAmount),
            sessions.Count
        );
    }

    public Dictionary<IRouletteService.BetType, string> GetBetTypeDisplayNames()
    {
        return new Dictionary<IRouletteService.BetType, string>
        {
            { IRouletteService.BetType.StraightUp, "Straight Up" },
            { IRouletteService.BetType.Red, "Red" },
            { IRouletteService.BetType.Black, "Black" },
            { IRouletteService.BetType.Even, "Even" },
            { IRouletteService.BetType.Odd, "Odd" },
            { IRouletteService.BetType.Low, "Low (1-18)" },
            { IRouletteService.BetType.High, "High (19-36)" },
            { IRouletteService.BetType.Dozen1, "1st Dozen (1-12)" },
            { IRouletteService.BetType.Dozen2, "2nd Dozen (13-24)" },
            { IRouletteService.BetType.Dozen3, "3rd Dozen (25-36)" },
            { IRouletteService.BetType.Column1, "1st Column" },
            { IRouletteService.BetType.Column2, "2nd Column" },
            { IRouletteService.BetType.Column3, "3rd Column" }
        };
    }

    public List<int> GetRedNumbers() => RED_NUMBERS.ToList();
    public List<int> GetBlackNumbers() => BLACK_NUMBERS.ToList();
}
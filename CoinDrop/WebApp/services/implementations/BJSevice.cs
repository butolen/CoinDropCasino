using CoinDrop;
using CoinDrop.services.interfaces;
using Domain;
using Microsoft.Extensions.Caching.Memory;

namespace WebApp.services.implementations;

public class BlackjackService : IBlackjackService
{
    private readonly IRepository<GameSession> _gameSessionRepo;
    private readonly IRepository<ApplicationUser> _userRepo;
    private readonly IRepository<Log> _logRepo;
    private readonly IRepository<SystemSetting> _systemSettingRepo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BlackjackService> _logger;
    
    private const string GAME_CACHE_KEY = "BlackjackGame_";
    private const int CACHE_DURATION_MINUTES = 30;
    private static readonly List<double> _defaultAllowedBets = new() { 1, 5, 10, 50, 100 };
    
    public BlackjackService(
        IRepository<GameSession> gameSessionRepo,
        IRepository<ApplicationUser> userRepo,
        IRepository<Log> logRepo,
        IRepository<SystemSetting> systemSettingRepo,
        IMemoryCache cache,
        ILogger<BlackjackService> logger)
    {
        _gameSessionRepo = gameSessionRepo;
        _userRepo = userRepo;
        _logRepo = logRepo;
        _systemSettingRepo = systemSettingRepo;
        _cache = cache;
        _logger = logger;
    }
    
    public async Task<BlackjackGame.GameResponse> StartNewGameAsync(int userId, double betAmount)
    {
        try
        {
            // Check if Blackjack is active
            var isActive = await GetBoolSettingAsync("blackjack_active", true);
            if (!isActive)
                return new BlackjackGame.GameResponse(false, "Blackjack is currently disabled");
            
            // Check if bet is allowed
            var allowedBets = await GetAllowedBetsListAsync();
            if (!allowedBets.Contains(betAmount))
                return new BlackjackGame.GameResponse(false, 
                    $"Invalid bet. Allowed: {string.Join(", ", allowedBets)}€");
            
            // Check user and TOTAL balance
            var user = await _userRepo.GetByIdAsync(u => u.Id == userId);
            if (user == null)
                return new BlackjackGame.GameResponse(false, "User not found");
            
            if (user.TotalBalance < betAmount)
                return new BlackjackGame.GameResponse(false, "Insufficient balance");
            
            // Check limits from settings
            var minBet = await GetDoubleSettingAsync("blackjack_min_bet", 1);
            var maxBet = await GetDoubleSettingAsync("blackjack_max_bet", 100);
            
            if (betAmount < minBet || betAmount > maxBet)
                return new BlackjackGame.GameResponse(false, 
                    $"Bet must be between {minBet}€ and {maxBet}€");
            
            // End existing game
            await EndExistingGameAsync(userId);
            
            // Create new game with real deck
            var game = new BlackjackGame
            {
                UserId = userId,
                BetAmount = betAmount,
                Status = GameStatus.Active
            };
            
            game.InitializeGame();
            
            _logger.LogInformation($"Game created for User {userId}. Bet: {betAmount}€, TotalBalance: {user.TotalBalance}");
            
            // Save original balances for logging
            var cryptoBefore = user.BalanceCrypto;
            var physicalBefore = user.BalancePhysical;
            var totalBefore = user.TotalBalance;
            
            // Betrag von Crypto zuerst abziehen, dann von Physical
            DeductBetFromUserBalances(user, betAmount);
            await _userRepo.UpdateAsync(user);
            
            _logger.LogInformation($"Bet deducted - Crypto: {cryptoBefore}€ -> {user.BalanceCrypto}€, Physical: {physicalBefore}€ -> {user.BalancePhysical}€");
            
            // Create GameSession for logging
            var gameSession = new GameSession
            {
                UserId = userId,
                GameType = GameType.Blackjack,
                BetAmount = betAmount,
                Result = GameResult.Loss,
                WinAmount = 0,
                BalanceBefore = totalBefore,
                BalanceAfter = user.TotalBalance,
                Timestamp = DateTime.UtcNow
            };
            
            await _gameSessionRepo.AddAsync(gameSession);
            
            // Save game in cache
            var cacheKey = GetCacheKey(userId);
            _cache.Set(cacheKey, game, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
            
            // Create log
            await _logRepo.AddAsync(new Log
            {
                ActionType = LogActionType.UserAction,
                UserType = LogUserType.User,
                UserId = userId,
                Description = $"Blackjack game started. Bet: {betAmount}€, BalanceBefore: {totalBefore}€, BalanceAfter: {user.TotalBalance}€"
            });
            
            return new BlackjackGame.GameResponse(true, "Game started", game.ToFrontendObject());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting Blackjack game");
            return new BlackjackGame.GameResponse(false, $"Error: {ex.Message}");
        }
    }
    
    public async Task<BlackjackGame.GameResponse> HitAsync(int userId)
    {
        try
        {
            var game = await GetGameFromCacheAsync(userId);
            if (game == null)
                return new BlackjackGame.GameResponse(false, "No active game found");
            
            if (game.Status != GameStatus.Active)
                return new BlackjackGame.GameResponse(false, "Game is not active");
            
            // DRAW REAL CARD
            game.ActivePlayerHand.Add(game.DrawCard());
            
            // Check for bust
            if (game.IsBusted(game.ActivePlayerHand))
            {
                await HandleGameEndAsync(game, GameStatus.PlayerBusted);
                return new BlackjackGame.GameResponse(true, "Bust! You lost", game.ToFrontendObject());
            }
            
            // Update game
            UpdateGameInCache(userId, game);
            
            return new BlackjackGame.GameResponse(true, "Card drawn", game.ToFrontendObject());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Hit");
            return new BlackjackGame.GameResponse(false, $"Error: {ex.Message}");
        }
    }
    
    public async Task<BlackjackGame.GameResponse> StandAsync(int userId)
    {
        try
        {
            var game = await GetGameFromCacheAsync(userId);
            if (game == null)
                return new BlackjackGame.GameResponse(false, "No active game found");
            
            if (game.Status != GameStatus.Active)
                return new BlackjackGame.GameResponse(false, "Game is not active");
            
            // DEALER DRAWS REAL CARDS
            game.DealerPlay();
            
            // GEWINNER BESTIMMEN
            game.DetermineWinner();
            
            // Spiel beenden und speichern
            await HandleGameEndAsync(game, game.Status);
            
            return new BlackjackGame.GameResponse(true, "Stand", game.ToFrontendObject());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Stand");
            return new BlackjackGame.GameResponse(false, $"Error: {ex.Message}");
        }
    }
    
    public async Task<BlackjackGame.GameResponse> DoubleDownAsync(int userId)
    {
        try
        {
            var game = await GetGameFromCacheAsync(userId);
            if (game == null)
                return new BlackjackGame.GameResponse(false, "No active game found");
            
            if (game.Status != GameStatus.Active)
                return new BlackjackGame.GameResponse(false, "Game is not active");
            
            if (!game.CanDoubleDown())
                return new BlackjackGame.GameResponse(false, "Double Down not allowed");
            
            // Check user and TOTAL balance
            var user = await _userRepo.GetByIdAsync(u => u.Id == userId);
            if (user == null || user.TotalBalance < game.BetAmount)
                return new BlackjackGame.GameResponse(false, "Insufficient balance for Double Down");
            
            var originalBet = game.BetAmount;
            
            _logger.LogInformation($"DoubleDown - User {userId}: Original bet: {originalBet}€, TotalBalance: {user.TotalBalance}€");
            
            // Save balances before deduction
            var cryptoBefore = user.BalanceCrypto;
            var physicalBefore = user.BalancePhysical;
            
            // Zusätzlichen Einsatz abziehen (Crypto zuerst)
            DeductBetFromUserBalances(user, originalBet);
            await _userRepo.UpdateAsync(user);
            
            // Double the bet
            game.BetAmount *= 2;
            
            _logger.LogInformation($"DoubleDown - Additional {originalBet}€ deducted. Crypto: {cryptoBefore}€ -> {user.BalanceCrypto}€, Physical: {physicalBefore}€ -> {user.BalancePhysical}€");
            _logger.LogInformation($"DoubleDown - Total bet now: {game.BetAmount}€");
            
            // DRAW ONE REAL CARD
            game.ActivePlayerHand.Add(game.DrawCard());
            
            // Check for bust
            if (game.IsBusted(game.ActivePlayerHand))
            {
                await HandleGameEndAsync(game, GameStatus.PlayerBusted);
                return new BlackjackGame.GameResponse(true, "Double Down - Bust! You lost", game.ToFrontendObject());
            }
            else
            {
                // Dealer plays
                game.DealerPlay();
                game.DetermineWinner();
                await HandleGameEndAsync(game, game.Status);
            }
            
            await LogActionAsync(userId, $"Double Down. Total bet: {game.BetAmount}€");
            return new BlackjackGame.GameResponse(true, "Double Down executed", game.ToFrontendObject());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Double Down");
            return new BlackjackGame.GameResponse(false, $"Error: {ex.Message}");
        }
    }
    
    public async Task<BlackjackGame.GameResponse> SplitAsync(int userId)
    {
        try
        {
            var game = await GetGameFromCacheAsync(userId);
            if (game == null)
                return new BlackjackGame.GameResponse(false, "No active game found");
            
            if (game.Status != GameStatus.Active)
                return new BlackjackGame.GameResponse(false, "Game is not active");
            
            if (!game.CanSplit())
                return new BlackjackGame.GameResponse(false, "Split not allowed");
            
            // Check user and TOTAL balance
            var user = await _userRepo.GetByIdAsync(u => u.Id == userId);
            if (user == null || user.TotalBalance < game.BetAmount)
                return new BlackjackGame.GameResponse(false, "Insufficient balance for Split");
            
            var originalBet = game.BetAmount;
            
            _logger.LogInformation($"Split - User {userId}: Original bet: {originalBet}€, TotalBalance: {user.TotalBalance}€");
            
            // Save balances before deduction
            var cryptoBefore = user.BalanceCrypto;
            var physicalBefore = user.BalancePhysical;
            
            // Zusätzlichen Einsatz für Split abziehen (Crypto zuerst)
            DeductBetFromUserBalances(user, originalBet);
            await _userRepo.UpdateAsync(user);
            
            // Der Gesamt-Einsatz ist jetzt 2x originalBet
            game.BetAmount = originalBet * 2;
            
            _logger.LogInformation($"Split - Additional {originalBet}€ deducted. Crypto: {cryptoBefore}€ -> {user.BalanceCrypto}€, Physical: {physicalBefore}€ -> {user.BalancePhysical}€");
            _logger.LogInformation($"Split - Total bet now: {game.BetAmount}€ (2x {originalBet}€)");
            
            // EXECUTE SPLIT
            game.SplitHand = new List<Card> { game.PlayerHand[1] };
            game.PlayerHand.RemoveAt(1);
            game.IsSplit = true;
            
            // GIVE ONE REAL CARD TO BOTH HANDS
            game.PlayerHand.Add(game.DrawCard());
            game.SplitHand.Add(game.DrawCard());
            
            // Wenn beide Hände Blackjack haben
            if (game.HasBlackjack(game.PlayerHand) && game.HasBlackjack(game.SplitHand))
            {
                // Beide Hände gewinnen 3:2
                game.Status = GameStatus.PlayerWon;
                // Jede Hand gewinnt originalBet * 2.5, also insgesamt originalBet * 5
                game.WinAmount = originalBet * 5;
                await HandleGameEndAsync(game, GameStatus.PlayerWon);
                return new BlackjackGame.GameResponse(true, "Double Blackjack! You win!", game.ToFrontendObject());
            }
            
            UpdateGameInCache(userId, game);
            
            await LogActionAsync(userId, $"Split. Total bet: {game.BetAmount}€ (2x {originalBet}€)");
            return new BlackjackGame.GameResponse(true, "Split executed", game.ToFrontendObject());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Split");
            return new BlackjackGame.GameResponse(false, $"Error: {ex.Message}");
        }
    }
    
    public async Task<BlackjackGame.GameResponse> SurrenderAsync(int userId)
    {
        try
        {
            var game = await GetGameFromCacheAsync(userId);
            if (game == null)
                return new BlackjackGame.GameResponse(false, "No active game found");
            
            if (game.Status != GameStatus.Active)
                return new BlackjackGame.GameResponse(false, "Game is not active");
            
            // Nur in der ersten Runde erlaubt
            if (game.PlayerHand.Count > 2)
                return new BlackjackGame.GameResponse(false, "Surrender only allowed in first round");
            
            // 50% des Einsatzes zurückgeben
            var surrenderAmount = game.BetAmount * 0.5;
            await HandleGameEndAsync(game, GameStatus.Surrendered, surrenderAmount);
            
            await LogActionAsync(userId, $"Surrender. Refund: {surrenderAmount}€");
            return new BlackjackGame.GameResponse(true, 
                $"Game surrendered. {surrenderAmount}€ refunded.", game.ToFrontendObject());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Surrender");
            return new BlackjackGame.GameResponse(false, $"Error: {ex.Message}");
        }
    }
    
    public async Task<BlackjackGame.GameResponse> GetGameStateAsync(int userId)
    {
        try
        {
            var game = await GetGameFromCacheAsync(userId);
    
            if (game == null)
            {
                // Return consistent structure
                return new BlackjackGame.GameResponse(true, "No active game", new
                {
                    HasActiveGame = false,
                    Game = (object?)null
                });
            }
    
            // IMPORTANT: hasActiveGame based on Game Status
            bool hasActiveGame = game.Status == GameStatus.Active;
    
            return new BlackjackGame.GameResponse(true, "Game state loaded", new
            {
                HasActiveGame = hasActiveGame,
                Game = game.ToFrontendObject()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting game state");
            return new BlackjackGame.GameResponse(false, $"Error: {ex.Message}");
        }
    }
    
    public async Task<BlackjackGame.GameResponse> GetAllowedBetsAsync()
    {
        try
        {
            var bets = await GetAllowedBetsListAsync();
            return new BlackjackGame.GameResponse(true, "Allowed bets", new
            {
                Bets = bets
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting allowed bets");
            return new BlackjackGame.GameResponse(false, $"Error: {ex.Message}");
        }
    }
    
    public async Task<BlackjackGame.GameResponse> GetGameRulesAsync()
    {
        try
        {
            var dealerStand = await GetIntSettingAsync("blackjack_dealer_stand", 17);
            var decksUsed = await GetIntSettingAsync("blackjack_decks", 6);
            
            var rules = new
            {
                DealerStandsOn = dealerStand,
                BlackjackPayout = "3:2",
                DoubleDownAllowed = "After first 2 cards",
                SplitAllowed = "With same value cards",
                SurrenderAllowed = "In first round (50% back)",
                DecksUsed = decksUsed,
                MinBet = await GetDoubleSettingAsync("blackjack_min_bet", 1),
                MaxBet = await GetDoubleSettingAsync("blackjack_max_bet", 100),
                IsActive = await GetBoolSettingAsync("blackjack_active", true)
            };
            
            return new BlackjackGame.GameResponse(true, "Game rules", rules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting game rules");
            return new BlackjackGame.GameResponse(false, $"Error: {ex.Message}");
        }
    }
    
    #region Private Methods
    
    // WICHTIG: Helper-Methode um Betrag von Balances abzuziehen (Crypto zuerst)
    private void DeductBetFromUserBalances(ApplicationUser user, double betAmount)
    {
        if (betAmount <= 0) return;
        
        _logger.LogInformation($"Deducting {betAmount}€ from User {user.Id}. Crypto: {user.BalanceCrypto}€, Physical: {user.BalancePhysical}€");
        
        // 1. Zuerst von Crypto abziehen
        if (user.BalanceCrypto >= betAmount)
        {
            user.BalanceCrypto -= betAmount;
            _logger.LogInformation($"Full amount deducted from Crypto. New Crypto: {user.BalanceCrypto}€");
        }
        else
        {
            // 2. Crypto nicht genug, Rest von Physical
            var cryptoDeduction = user.BalanceCrypto;
            var remaining = betAmount - cryptoDeduction;
            
            user.BalanceCrypto = 0;
            user.BalancePhysical -= remaining;
            
            _logger.LogInformation($"Deducted {cryptoDeduction}€ from Crypto, {remaining}€ from Physical. New Crypto: 0€, Physical: {user.BalancePhysical}€");
        }
    }
    
    // WICHTIG: Helper-Methode um Gewinn zu Balances hinzuzufügen (50/50)
    private void AddWinToUserBalances(ApplicationUser user, double winAmount)
    {
        if (winAmount <= 0) return;
        
        _logger.LogInformation($"Adding {winAmount}€ win to User {user.Id}. Crypto: {user.BalanceCrypto}€, Physical: {user.BalancePhysical}€");
        
        // Gewinn 50/50 aufteilen
        var half = winAmount / 2;
        user.BalanceCrypto += half;
        user.BalancePhysical += half;
        
        _logger.LogInformation($"Added {half}€ to Crypto and Physical. New Crypto: {user.BalanceCrypto}€, Physical: {user.BalancePhysical}€");
    }
    
    private async Task HandleGameEndAsync(BlackjackGame game, GameStatus status, double? customWinAmount = null)
    {
        game.Status = status;
        game.EndedAt = DateTime.UtcNow;
        
        double winAmount = CalculateWinAmount(game, status, customWinAmount);
        game.WinAmount = winAmount;
        
        var user = await _userRepo.GetByIdAsync(u => u.Id == game.UserId);
        if (user != null)
        {
            var balanceBefore = user.TotalBalance;
            
            _logger.LogInformation($"Game end for User {game.UserId}. Status: {status}, TotalBet: {game.BetAmount}€, Win: {winAmount}€, BalanceBefore: {balanceBefore}€");
            
            if (winAmount > 0)
            {
                // Gewinn zu Balances hinzufügen (50/50)
                AddWinToUserBalances(user, winAmount);
            }
            
            var balanceAfter = user.TotalBalance;
            
            _logger.LogInformation($"After settlement - BalanceAfter: {balanceAfter}€, Crypto: {user.BalanceCrypto}€, Physical: {user.BalancePhysical}€");
            
            // GameSession updaten
            var gameSession = await GetLatestGameSession(game.UserId);
            
            if (gameSession != null)
            {
                gameSession.Result = GetGameResult(status);
                gameSession.WinAmount = winAmount;
                gameSession.BalanceAfter = balanceAfter;
                await _gameSessionRepo.UpdateAsync(gameSession);
                
                _logger.LogInformation($"Updated GameSession: BalanceBefore: {gameSession.BalanceBefore}€, BalanceAfter: {gameSession.BalanceAfter}€, WinAmount: {gameSession.WinAmount}€");
            }
            
            // User aktualisieren
            await _userRepo.UpdateAsync(user);
        }
        
        // Cache entfernen
        _cache.Remove(GetCacheKey(game.UserId));
        
        await LogActionAsync(game.UserId, 
            $"Game ended. Status: {status}, Bet: {game.BetAmount}€, Win: {winAmount}€");
    }
    
    private double CalculateWinAmount(BlackjackGame game, GameStatus status, double? customWinAmount)
    {
        // Für Split: game.BetAmount ist der GESAMT-Einsatz (2x originalBet)
        // Wir müssen den Gewinn basierend auf dem ORIGINAL Einsatz pro Hand berechnen
        
        double originalBetPerHand;
        if (game.IsSplit)
        {
            // Bei Split ist game.BetAmount = 2x originalBet
            originalBetPerHand = game.BetAmount / 2;
        }
        else
        {
            originalBetPerHand = game.BetAmount;
        }
        
        double winAmount = status switch
        {
            GameStatus.Blackjack => originalBetPerHand * 2.5,          // 3:2 payout pro Hand
            GameStatus.PlayerWon => originalBetPerHand * 2,            // 1:1 payout pro Hand
            GameStatus.DealerBusted => originalBetPerHand * 2,         // 1:1 payout pro Hand
            GameStatus.Draw => originalBetPerHand,                     // Bet returned pro Hand
            GameStatus.Surrendered => customWinAmount ?? originalBetPerHand * 0.5, // 50% back pro Hand
            _ => 0                                                     // Lost
        };
        
        // Bei Split: Multipliziere mit Anzahl der Hände
        if (game.IsSplit && winAmount > 0)
        {
            winAmount *= 2; // Zwei Hände
        }
        
        _logger.LogInformation($"CalculateWinAmount - Status: {status}, IsSplit: {game.IsSplit}, OriginalBetPerHand: {originalBetPerHand}€, TotalWin: {winAmount}€");
        
        return winAmount;
    }
    
    private async Task<List<double>> GetAllowedBetsListAsync()
    {
        return _defaultAllowedBets;
    }
    
    private async Task<double> GetDoubleSettingAsync(string key, double defaultValue)
    {
        try
        {
            var setting = await _systemSettingRepo.GetByIdAsync(
                s => s.Category == "GameConfig" && s.SettingKey == key);
            
            if (setting != null && double.TryParse(setting.SettingValue, out var value))
                return value;
        }
        catch { }
        
        return defaultValue;
    }
    
    private async Task<int> GetIntSettingAsync(string key, int defaultValue)
    {
        try
        {
            var setting = await _systemSettingRepo.GetByIdAsync(
                s => s.Category == "GameConfig" && s.SettingKey == key);
            
            if (setting != null && int.TryParse(setting.SettingValue, out var value))
                return value;
        }
        catch { }
        
        return defaultValue;
    }
    
    private async Task<bool> GetBoolSettingAsync(string key, bool defaultValue)
    {
        try
        {
            var setting = await _systemSettingRepo.GetByIdAsync(
                s => s.Category == "GameConfig" && s.SettingKey == key);
            
            if (setting != null && bool.TryParse(setting.SettingValue, out var value))
                return value;
        }
        catch { }
        
        return defaultValue;
    }
    
    private GameResult GetGameResult(GameStatus status)
    {
        return status switch
        {
            GameStatus.Blackjack or GameStatus.PlayerWon or GameStatus.DealerBusted => GameResult.Win,
            GameStatus.PlayerBusted or GameStatus.DealerWon => GameResult.Loss,
            _ => GameResult.Draw
        };
    }
    
    private async Task<GameSession?> GetLatestGameSession(int userId)
    {
        try
        {
            var results = await _gameSessionRepo.ExecuteQueryAsync(q =>
                q.Where(gs => gs.UserId == userId && gs.GameType == GameType.Blackjack)
                    .OrderByDescending(gs => gs.Timestamp)
                    .Take(1)
            );
            
            return results.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest game session");
            return null;
        }
    }
    
    private async Task<BlackjackGame?> GetGameFromCacheAsync(int userId)
    {
        return _cache.Get<BlackjackGame>(GetCacheKey(userId));
    }
    
    private void UpdateGameInCache(int userId, BlackjackGame game)
    {
        _cache.Set(GetCacheKey(userId), game, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
    }
    
    private async Task EndExistingGameAsync(int userId)
    {
        var existingGame = await GetGameFromCacheAsync(userId);
        if (existingGame != null && existingGame.Status == GameStatus.Active)
        {
            await HandleGameEndAsync(existingGame, GameStatus.DealerWon);
        }
    }
    
    private async Task LogActionAsync(int userId, string description)
    {
        await _logRepo.AddAsync(new Log
        {
            ActionType = LogActionType.UserAction,
            UserType = LogUserType.User,
            UserId = userId,
            Description = $"Blackjack: {description}"
        });
    }
    
    private string GetCacheKey(int userId) => $"{GAME_CACHE_KEY}{userId}";
    
    #endregion
}
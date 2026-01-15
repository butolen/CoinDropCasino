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
            
            // Check user and TOTAL balance (nur prüfen, nicht abziehen!)
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
            
            _logger.LogInformation($"Starting new game for User {userId}. Bet: {betAmount}€, TotalBalance: {user.TotalBalance}€");
            
            // Create GameSession for logging (Bevor Spiel erstellt wird)
            var gameSession = new GameSession
            {
                UserId = userId,
                GameType = GameType.Blackjack,
                BetAmount = betAmount,
                Result = GameResult.Loss, // Default, wird bei Spielende aktualisiert
                WinAmount = 0,
                BalanceBefore = user.TotalBalance,
                BalanceAfter = user.TotalBalance, // Wird bei Spielende aktualisiert
                Timestamp = DateTime.UtcNow
            };
            
            await _gameSessionRepo.AddAsync(gameSession);
            
            // WICHTIG: GameSession ID speichern
            int gameSessionId = gameSession.SessionId;
            
            _logger.LogInformation($"Created GameSession with ID: {gameSessionId}");
            
            // Create new game with real deck
            var game = new BlackjackGame
            {
                UserId = userId,
                BetAmount = betAmount,
                Status = GameStatus.Active,
                GameSessionId = gameSessionId
            };
            
            // Spiel initialisieren (inkl. Deck und Karten)
            game.InitializeGame();
            
            // SOFORT auf Blackjack prüfen
            if (game.HasBlackjack(game.PlayerHand))
            {
                _logger.LogInformation($"Player has Blackjack! Ending game immediately.");
                
                // Blackjack sofort behandeln (kein Einsatz-Abzug!)
                await HandleGameEndAsync(game, GameStatus.Blackjack);
                
                return new BlackjackGame.GameResponse(true, "Blackjack! You win!", game.ToFrontendObject());
            }
            
            // Save game in cache
            var cacheKey = GetCacheKey(userId);
            _cache.Set(cacheKey, game, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
            
            // Create log
            await _logRepo.AddAsync(new Log
            {
                ActionType = LogActionType.UserAction,
                UserType = LogUserType.User,
                UserId = userId,
                Description = $"Blackjack game started. Bet: {betAmount}€ reserved, Session: {gameSessionId}"
            });
            
            _logger.LogInformation($"Game started successfully. Session: {gameSessionId}, Balance remains: {user.TotalBalance}€");
            
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
            
            // ECHTE KARTE ZIEHEN
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
            
            // Bei Split: Zwischen Händen wechseln
            if (game.IsSplit && !game.IsSplitActive)
            {
                game.IsSplitActive = true;
                UpdateGameInCache(userId, game);
                
                _logger.LogInformation($"Split: Switching to second hand. First hand value: {game.GetHandValue(game.PlayerHand)}");
                return new BlackjackGame.GameResponse(true, "Now playing second hand", game.ToFrontendObject());
            }
            
            // DEALER ZIEHT ECHTE KARTEN
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
            
            // Check user and TOTAL balance für zusätzlichen Einsatz
            var user = await _userRepo.GetByIdAsync(u => u.Id == userId);
            if (user == null || user.TotalBalance < game.BetAmount)
                return new BlackjackGame.GameResponse(false, "Insufficient balance for Double Down");
            
            // Logge die GameSession ID
            _logger.LogInformation($"DoubleDown - User {userId}, GameSessionId: {game.GameSessionId}, Current bet: {game.BetAmount}€");
            
            // Double the bet (nur im Spiel-Objekt, nicht in Balance!)
            var originalBet = game.BetAmount;
            game.BetAmount *= 2;
            
            _logger.LogInformation($"DoubleDown - User {userId}: Bet doubled from {originalBet}€ to {game.BetAmount}€");
            
            // EINE ECHTE KARTE ZIEHEN
            game.ActivePlayerHand.Add(game.DrawCard());
            
            // Check for bust
            if (game.IsBusted(game.ActivePlayerHand))
            {
                await HandleGameEndAsync(game, GameStatus.PlayerBusted);
                return new BlackjackGame.GameResponse(true, "Double Down - Bust! You lost", game.ToFrontendObject());
            }
            else
            {
                // Dealer zieht Karten
                game.DealerPlay();
                game.DetermineWinner();
                await HandleGameEndAsync(game, game.Status);
            }
            
            await LogActionAsync(userId, $"Double Down. Total bet: {game.BetAmount}€, GameSessionId: {game.GameSessionId}");
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
            
            // Check user and TOTAL balance für zusätzlichen Einsatz
            var user = await _userRepo.GetByIdAsync(u => u.Id == userId);
            if (user == null || user.TotalBalance < game.BetAmount)
                return new BlackjackGame.GameResponse(false, "Insufficient balance for Split");
            
            var originalBet = game.BetAmount;
            
            _logger.LogInformation($"Split - User {userId}, GameSessionId: {game.GameSessionId}, Bet will be doubled to {originalBet * 2}€");
            
            // Der Gesamt-Einsatz ist jetzt 2x originalBet
            game.BetAmount = originalBet * 2;
            
            // EXECUTE SPLIT
            game.SplitHand = new List<Card> { game.PlayerHand[1] };
            game.PlayerHand.RemoveAt(1);
            game.IsSplit = true;
            game.IsSplitActive = false; // Zuerst erster Hand spielen
            
            _logger.LogInformation($"Split - Created two hands. First hand: {game.PlayerHand.Count} cards, Second hand: {game.SplitHand.Count} cards");
            
            // JEDER HAND EINE ECHTE KARTE GEBEN
            game.PlayerHand.Add(game.DrawCard());
            game.SplitHand.Add(game.DrawCard());
            
            _logger.LogInformation($"Split - After drawing cards. First hand value: {game.GetHandValue(game.PlayerHand)}, Second hand value: {game.GetHandValue(game.SplitHand)}");
            
            // Prüfen ob erste Hand Blackjack hat
            if (game.HasBlackjack(game.PlayerHand))
            {
                _logger.LogInformation($"Split - First hand has Blackjack! Value: 21");
            }
            
            // Prüfen ob zweite Hand Blackjack hat
            if (game.HasBlackjack(game.SplitHand))
            {
                _logger.LogInformation($"Split - Second hand has Blackjack! Value: 21");
            }
            
            // Wenn beide Hände Blackjack haben - sofort auswerten
            if (game.HasBlackjack(game.PlayerHand) && game.HasBlackjack(game.SplitHand))
            {
                _logger.LogInformation($"Split - BOTH HANDS HAVE BLACKJACK! Ending game immediately.");
                await HandleGameEndAsync(game, GameStatus.PlayerWon);
                return new BlackjackGame.GameResponse(true, "Double Blackjack! You win!", game.ToFrontendObject());
            }
            
            // Wenn eine Hand Blackjack hat und die andere nicht, weiterspielen
            if (game.HasBlackjack(game.PlayerHand))
            {
                _logger.LogInformation($"Split - First hand has Blackjack, playing second hand");
                game.IsSplitActive = true; // Direkt zur zweiten Hand wechseln
            }
            
            UpdateGameInCache(userId, game);
            
            await LogActionAsync(userId, $"Split. Total bet: {game.BetAmount}€ (2x {originalBet}€), GameSessionId: {game.GameSessionId}");
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
            
            // Spiel mit 50% Verlust beenden
            await HandleGameEndAsync(game, GameStatus.Surrendered);
            
            await LogActionAsync(userId, $"Surrender. 50% of bet lost.");
            return new BlackjackGame.GameResponse(true, 
                $"Game surrendered. 50% of bet lost.", game.ToFrontendObject());
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
                SurrenderAllowed = "In first round (50% loss)",
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
        
        // Berechne den Gewinn basierend auf Spielstatus und Einsatz
        double winAmount = CalculateWinAmount(game, status, customWinAmount);
        game.WinAmount = winAmount;
        
        var user = await _userRepo.GetByIdAsync(u => u.Id == game.UserId);
        if (user != null)
        {
            var balanceBefore = user.TotalBalance;
            
            _logger.LogInformation($"Game end for User {game.UserId}. Status: {status}, TotalBet: {game.BetAmount}€, CalculatedWin: {winAmount}€, BalanceBefore: {balanceBefore}€, GameSessionId: {game.GameSessionId}");
            
            // JETZT die finale Balance-Berechnung basierend auf Spielausgang
            double netAmount = 0;
            
            switch (status)
            {
                case GameStatus.Blackjack:
                    // Bei Blackjack: Nur Gewinn hinzufügen (Einsatz wurde nie abgezogen)
                    AddWinToUserBalances(user, winAmount);
                    netAmount = winAmount;
                    _logger.LogInformation($"Blackjack! Added {winAmount}€ win (no bet deducted).");
                    break;
                    
                case GameStatus.PlayerWon:
                case GameStatus.DealerBusted:
                    // Bei normalem Gewinn: Nettogewinn hinzufügen
                    AddWinToUserBalances(user, winAmount);
                    netAmount = winAmount;
                    _logger.LogInformation($"Regular win. Added {winAmount}€ win.");
                    break;
                    
                case GameStatus.Draw:
                    // Bei Unentschieden: Nichts tun (Einsatz bleibt)
                    netAmount = 0;
                    _logger.LogInformation($"Draw. No balance change (bet remains).");
                    break;
                    
                case GameStatus.Surrendered:
                    // Bei Surrender: Nur die Hälfte des Einsatzes abziehen
                    var surrenderLoss = game.BetAmount * 0.5;
                    DeductBetFromUserBalances(user, surrenderLoss);
                    netAmount = -surrenderLoss;
                    _logger.LogInformation($"Surrender. Deducted {surrenderLoss}€ (50% of bet).");
                    break;
                    
                case GameStatus.PlayerBusted:
                case GameStatus.DealerWon:
                    // Bei Verlust: Den vollen Einsatz abziehen
                    DeductBetFromUserBalances(user, game.BetAmount);
                    netAmount = -game.BetAmount;
                    _logger.LogInformation($"Loss. Deducted full bet: {game.BetAmount}€");
                    break;
            }
            
            var balanceAfter = user.TotalBalance;
            
            _logger.LogInformation($"After settlement - BalanceAfter: {balanceAfter}€, Crypto: {user.BalanceCrypto}€, Physical: {user.BalancePhysical}€");
            
            // GameSession updaten mit der gespeicherten ID
            if (game.GameSessionId > 0)
            {
                var gameSession = await GetGameSessionById(game.GameSessionId);
                
                if (gameSession != null)
                {
                    gameSession.Result = GetGameResult(status);
                    gameSession.WinAmount = Math.Max(0, netAmount); // Nur positive Werte als WinAmount
                    gameSession.BalanceAfter = balanceAfter;
                    await _gameSessionRepo.UpdateAsync(gameSession);
                    
                    _logger.LogInformation($"Updated GameSession {gameSession.SessionId}: BalanceBefore: {gameSession.BalanceBefore}€, BalanceAfter: {gameSession.BalanceAfter}€, NetChange: {netAmount}€");
                }
                else
                {
                    _logger.LogWarning($"GameSession {game.GameSessionId} not found for update!");
                }
            }
            else
            {
                _logger.LogError($"No GameSessionId found in game object!");
            }
            
            // User aktualisieren
            await _userRepo.UpdateAsync(user);
        }
        else
        {
            _logger.LogError($"User {game.UserId} not found for game end settlement!");
        }
        
        // Cache entfernen
        _cache.Remove(GetCacheKey(game.UserId));
        
        await LogActionAsync(game.UserId, 
            $"Game ended. Status: {status}, Bet: {game.BetAmount}€, WinAmount: {winAmount}€");
    }
    
    private double CalculateWinAmount(BlackjackGame game, GameStatus status, double? customWinAmount)
    {
        // Berechnet den GEWINN (ohne Berücksichtigung des ursprünglichen Einsatzes)
        // Bei Split: Jede Hand wird separat behandelt
        double winAmount = 0;
        
        if (game.IsSplit)
        {
            // Bei Split: Jede Hand hat originalBet (BetAmount ist 2x originalBet)
            double originalBetPerHand = game.BetAmount / 2;
            
            switch (status)
            {
                case GameStatus.Blackjack:
                    winAmount = originalBetPerHand * 1.5 * 2; // Beide Hände Blackjack
                    break;
                case GameStatus.PlayerWon:
                case GameStatus.DealerBusted:
                    winAmount = originalBetPerHand * 2; // Beide Hände gewinnen
                    break;
                case GameStatus.Draw:
                    winAmount = 0; // Beide Hände Unentschieden
                    break;
                case GameStatus.Surrendered:
                    winAmount = originalBetPerHand * 0.5 * 2; // Beide Hände 50% Verlust
                    break;
                default:
                    winAmount = 0; // Verlust
                    break;
            }
        }
        else
        {
            // Normales Spiel (kein Split)
            winAmount = status switch
            {
                GameStatus.Blackjack => game.BetAmount * 1.5,          // 3:2 payout
                GameStatus.PlayerWon => game.BetAmount,                // 1:1 payout
                GameStatus.DealerBusted => game.BetAmount,             // 1:1 payout
                GameStatus.Draw => 0,                                  // Kein Gewinn
                GameStatus.Surrendered => customWinAmount ?? game.BetAmount * 0.5, // 50% Verlust
                _ => 0                                                 // Kein Gewinn bei Verlust
            };
        }
        
        _logger.LogInformation($"CalculateWinAmount - Status: {status}, Bet: {game.BetAmount}€, IsSplit: {game.IsSplit}, Win: {winAmount}€");
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
    
    private async Task<GameSession?> GetGameSessionById(int id)
    {
        try
        {
            return await _gameSessionRepo.GetByIdAsync(gs => gs.SessionId == id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting game session by ID {id}");
            return null;
        }
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
            // Setze Spiel als verloren
            existingGame.Status = GameStatus.DealerWon;
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
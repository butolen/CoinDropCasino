namespace CoinDrop.services.interfaces;

public interface IBlackjackService
{
    Task<BlackjackGame.GameResponse> StartNewGameAsync(int userId, double betAmount);
    Task<BlackjackGame.GameResponse> HitAsync(int userId);
    Task<BlackjackGame.GameResponse> StandAsync(int userId);
    Task<BlackjackGame.GameResponse> DoubleDownAsync(int userId);
    Task<BlackjackGame.GameResponse> SplitAsync(int userId);
    Task<BlackjackGame.GameResponse> SurrenderAsync(int userId);
    Task<BlackjackGame.GameResponse> GetGameStateAsync(int userId);
    Task<BlackjackGame.GameResponse> GetAllowedBetsAsync();
    Task<BlackjackGame.GameResponse> GetGameRulesAsync();
}
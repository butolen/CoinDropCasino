using CoinDrop;
using CoinDrop.services.interfaces;
using Domain;
using Microsoft.EntityFrameworkCore;
using WebApp.services.dtos;

namespace WebApp.services.implementations;

public sealed class GameHistoryService : IGameHistoryService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public GameHistoryService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<PagedResultHistory<GameHistoryRowDto>> GetForUserAsync(
        int userId,
        GameHistoryQuery query,
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var gameSessionRepo = scope.ServiceProvider.GetRequiredService<GameSessionRepo>();

        IQueryable<GameSession> baseQuery = gameSessionRepo.Query()
            .AsNoTracking()
            .Where(gs => gs.UserId == userId);

        // Game Type filter
        if (query.GameType != GameTypeFilter.All)
        {
            var gameType = query.GameType == GameTypeFilter.Blackjack 
                ? GameType.Blackjack 
                : GameType.Roulette;
            baseQuery = baseQuery.Where(gs => gs.GameType == gameType);
        }

        // Result filter
        if (query.Result != GameResultFilter.All)
        {
            var result = query.Result switch
            {
                GameResultFilter.Win => GameResult.Win,
                GameResultFilter.Loss => GameResult.Loss,
                GameResultFilter.Draw => GameResult.Draw,
                _ => GameResult.Win
            };
            baseQuery = baseQuery.Where(gs => gs.Result == result);
        }

        // Date range filter
        if (query.FromUtc.HasValue)
            baseQuery = baseQuery.Where(gs => gs.Timestamp >= query.FromUtc.Value);

        if (query.ToUtc.HasValue)
            baseQuery = baseQuery.Where(gs => gs.Timestamp <= query.ToUtc.Value);

        // Min Bet Amount filter
        if (query.MinBetAmount.HasValue)
            baseQuery = baseQuery.Where(gs => gs.BetAmount >= query.MinBetAmount.Value);

        // Min Win Amount filter
        if (query.MinWinAmount.HasValue)
            baseQuery = baseQuery.Where(gs => gs.WinAmount >= query.MinWinAmount.Value);

        // Projektion auf DTO
        var projected = baseQuery
            .OrderByDescending(gs => gs.Timestamp)
            .Select(gs => new GameHistoryRowDto
            {
                Game = gs.GameType.ToString(),
                BetAmount = gs.BetAmount,
                Result = gs.Result.ToString(),
                WinAmount = gs.WinAmount,
                TimestampUtc = gs.Timestamp,
                BalanceBefore = gs.BalanceBefore,
                BalanceAfter = gs.BalanceAfter
            });

        var items = await projected.ToArrayAsync(ct);

        return new PagedResultHistory<GameHistoryRowDto>
        {
            Items = items,
            TotalCount = items.Length,
            Page = 1,
            PageSize = items.Length
        };
    }
}

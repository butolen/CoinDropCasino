using WebApp.services.dtos;

namespace CoinDrop.services.interfaces;

public interface IGameHistoryService
{
    Task<PagedResultHistory<GameHistoryRowDto>> GetForUserAsync(
        int userId,
        GameHistoryQuery query,
        CancellationToken ct = default);
}
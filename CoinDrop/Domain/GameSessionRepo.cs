using CoinDrop;

namespace Domain;

using Microsoft.EntityFrameworkCore;

public class GameSessionRepo : ARepository<GameSession>
{
    public GameSessionRepo(CoinDropContext ctx) : base(ctx) { }

    public Task<List<GameSession>> GetByUserAsync(int userId, CancellationToken ct = default)
        => Query().Where(g => g.UserId == userId)
            .OrderByDescending(g => g.Timestamp)
            .ToListAsync(ct);
}
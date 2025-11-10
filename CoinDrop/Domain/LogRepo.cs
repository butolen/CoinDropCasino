using CoinDrop;

namespace Domain;

using Microsoft.EntityFrameworkCore;

public class LogRepo : ARepository<Log>
{
    public LogRepo(CoinDropContext ctx) : base(ctx) { }

    public Task<List<Log>> RecentAsync(int take = 100, CancellationToken ct = default)
        => Query().OrderByDescending(l => l.Date).Take(take).ToListAsync(ct);
}
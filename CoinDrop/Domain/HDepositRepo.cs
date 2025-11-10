using CoinDrop;

namespace Domain;

using Microsoft.EntityFrameworkCore;

public class HDepositRepo : ARepository<HardwareDeposit>
{
    public HDepositRepo(CoinDropContext ctx) : base(ctx) { }

    public Task<List<HardwareDeposit>> GetPendingByUserAsync(int userId, CancellationToken ct = default)
        => Query().Where(h => h.UserId == userId && !h.Confirmed)
            .OrderByDescending(h => h.Timestamp)
            .ToListAsync(ct);
}
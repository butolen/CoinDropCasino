using CoinDrop;

namespace Domain;


using Microsoft.EntityFrameworkCore;

public class WithdrawalRepo : ARepository<Withdrawal>
{
    public WithdrawalRepo(CoinDropContext ctx) : base(ctx) { }

    public Task<List<Withdrawal>> GetOpenByUserAsync(int userId, CancellationToken ct = default)
        => Query().Where(w => w.UserId == userId && (w.Status == WithdrawalStatus.Pending || w.Status == WithdrawalStatus.Approved))
            .OrderByDescending(w => w.Timestamp)
            .ToListAsync(ct);
}
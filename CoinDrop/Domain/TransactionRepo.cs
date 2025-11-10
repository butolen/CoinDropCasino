using CoinDrop;

namespace Domain;

using Microsoft.EntityFrameworkCore;

public class TransactionRepo : ARepository<Transaction>
{
    public TransactionRepo(CoinDropContext ctx) : base(ctx) { }

    public Task<List<Transaction>> GetByUserAsync(int userId, CancellationToken ct = default)
        => Query().Where(t => t.UserId == userId)
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync(ct);
}
using CoinDrop;

namespace Domain;

using Microsoft.EntityFrameworkCore;

public class CDepositRepo : ARepository<CryptoDeposit>
{
    public CDepositRepo(CoinDropContext ctx) : base(ctx) { }

    public Task<List<CryptoDeposit>> GetByUserAsync(int userId, CancellationToken ct = default)
        => Query().Where(c => c.UserId == userId)
            .OrderByDescending(c => c.Timestamp)
            .ToListAsync(ct);

    public Task<CryptoDeposit?> GetByTxHashAsync(string txHash, CancellationToken ct = default)
        => Query().FirstOrDefaultAsync(c => c.TxHash == txHash, ct);
}
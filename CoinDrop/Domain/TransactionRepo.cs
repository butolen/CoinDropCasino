using CoinDrop;

namespace Domain;

using Microsoft.EntityFrameworkCore;

public class TransactionRepo : ARepository<Transaction>
{
    public TransactionRepo(IDbContextFactory<CoinDropContext> contextFactory) 
        : base(contextFactory)
    {
    }
}
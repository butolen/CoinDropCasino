using CoinDrop;

namespace Domain;

using Microsoft.EntityFrameworkCore;

public class TransactionRepo : ARepository<Transaction>
{
    public TransactionRepo(CoinDropContext context) : base(context)
    {
    }
}
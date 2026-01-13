using CoinDrop;

namespace Domain;


using Microsoft.EntityFrameworkCore;

public class WithdrawalRepo : ARepository<Withdrawal>
{
    public WithdrawalRepo(IDbContextFactory<CoinDropContext> contextFactory) 
        : base(contextFactory) { }


}
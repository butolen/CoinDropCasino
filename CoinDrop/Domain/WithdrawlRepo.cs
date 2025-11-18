using CoinDrop;

namespace Domain;


using Microsoft.EntityFrameworkCore;

public class WithdrawalRepo : ARepository<Withdrawal>
{
    public WithdrawalRepo(CoinDropContext ctx) : base(ctx) { }


}
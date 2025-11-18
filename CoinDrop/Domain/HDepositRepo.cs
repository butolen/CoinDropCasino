using CoinDrop;

namespace Domain;

using Microsoft.EntityFrameworkCore;

public class HDepositRepo : ARepository<HardwareDeposit>
{
    public HDepositRepo(CoinDropContext ctx) : base(ctx) { }

    
}
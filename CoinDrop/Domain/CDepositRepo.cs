using CoinDrop;

namespace Domain;

using Microsoft.EntityFrameworkCore;

public class CDepositRepo : ARepository<CryptoDeposit>
{
    public CDepositRepo(CoinDropContext ctx) : base(ctx) { }

  
}
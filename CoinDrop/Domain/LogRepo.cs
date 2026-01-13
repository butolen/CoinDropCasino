using CoinDrop;

namespace Domain;

using Microsoft.EntityFrameworkCore;

public class LogRepo : ARepository<Log>
{
    public LogRepo(IDbContextFactory<CoinDropContext> contextFactory) 
        : base(contextFactory) { }


}
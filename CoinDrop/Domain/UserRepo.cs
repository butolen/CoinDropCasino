using CoinDrop;
using Microsoft.EntityFrameworkCore;

namespace Domain;

public class UserRepo : ARepository<ApplicationUser>
{
    public UserRepo(IDbContextFactory<CoinDropContext> contextFactory) 
        : base(contextFactory)
    {
    }
    
}
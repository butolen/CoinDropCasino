using CoinDrop;

namespace Domain;

public class UserRepo : ARepository<ApplicationUser>
{
    public UserRepo(CoinDropContext context) : base(context)
    {
    }
    
}
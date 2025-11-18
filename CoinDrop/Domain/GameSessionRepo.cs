using CoinDrop;

namespace Domain;

using Microsoft.EntityFrameworkCore;

public class GameSessionRepo : ARepository<GameSession>
{
    public GameSessionRepo(CoinDropContext ctx) : base(ctx) { }


}
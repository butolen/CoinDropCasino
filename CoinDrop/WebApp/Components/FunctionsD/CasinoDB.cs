using CoinDrop;
using Microsoft.EntityFrameworkCore;


namespace WebApp.Data
{
    public class CasinoDb : DbContext
    {
        public CasinoDb(DbContextOptions<CasinoDb> options)
            : base(options)
        {
            
        }

        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<Withdrawal> Withdrawls { get; set; }
        public DbSet<Log> Logs { get; set; }
        public DbSet<ApplicationUser> Deposits { get; set; }
        public DbSet<RouletteStat> RouletteStats { get; set; }
    }
}

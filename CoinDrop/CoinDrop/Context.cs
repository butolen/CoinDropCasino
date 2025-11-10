namespace CoinDrop;

using Microsoft.EntityFrameworkCore;

public class CoinDropContext : DbContext
{
    public CoinDropContext(DbContextOptions<CoinDropContext> options) : base(options) { }

    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<HardwareDeposit> HardwareDeposits => Set<HardwareDeposit>();
    public DbSet<CryptoDeposit> CryptoDeposits => Set<CryptoDeposit>();
    public DbSet<Withdrawal> Withdrawals => Set<Withdrawal>();
    public DbSet<Log> Logs => Set<Log>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // --- Beziehungen ApplicationUser ---
        modelBuilder.Entity<ApplicationUser>()
            .HasMany(u => u.Transactions)
            .WithOne(t => t.User)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ApplicationUser>()
            .HasMany(u => u.GameSessions)
            .WithOne(g => g.User)
            .HasForeignKey(g => g.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ApplicationUser>()
            .HasMany(u => u.HardwareDeposits)
            .WithOne(h => h.User)
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ApplicationUser>()
            .HasMany(u => u.CryptoDeposits)
            .WithOne(c => c.User)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ApplicationUser>()
            .HasMany(u => u.WithdrawalRequests)
            .WithOne(w => w.User)
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Log optional (User kann null sein)
        modelBuilder.Entity<Log>()
            .HasOne(l => l.User)
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // --- Enum-Konvertierungen ---
        modelBuilder.Entity<Transaction>()
            .Property(t => t.Type)
            .HasConversion<string>();

        modelBuilder.Entity<Transaction>()
            .Property(t => t.SourceBalance)
            .HasConversion<string>();

        modelBuilder.Entity<CryptoDeposit>()
            .Property(c => c.Status)
            .HasConversion<string>();

        modelBuilder.Entity<GameSession>()
            .Property(g => g.GameType)
            .HasConversion<string>();

        modelBuilder.Entity<GameSession>()
            .Property(g => g.Result)
            .HasConversion<string>();

        modelBuilder.Entity<Withdrawal>()
            .Property(w => w.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Log>()
            .Property(l => l.ActionType)
            .HasConversion<string>();

        modelBuilder.Entity<Log>()
            .Property(l => l.UserType)
            .HasConversion<string>();
    }
}
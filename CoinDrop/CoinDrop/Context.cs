
using CoinDrop;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public class CoinDropContext
    : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
{
    public CoinDropContext(DbContextOptions<CoinDropContext> options) : base(options) { }

    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<HardwareDeposit> HardwareDeposits => Set<HardwareDeposit>();
    public DbSet<CryptoDeposit> CryptoDeposits => Set<CryptoDeposit>();
    public DbSet<Withdrawal> Withdrawals => Set<Withdrawal>();
    public DbSet<Log> Logs => Set<Log>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Wichtig: erst die Identity-Basis mappen lassen
        base.OnModelCreating(b);

        // ---------- Identity-Tabellen auf lowercase ----------
        b.Entity<ApplicationUser>().ToTable("user");
        b.Entity<IdentityRole<int>>().ToTable("role");
        b.Entity<IdentityUserRole<int>>().ToTable("user_role");
        b.Entity<IdentityUserClaim<int>>().ToTable("user_claim");
        b.Entity<IdentityUserLogin<int>>().ToTable("user_login");
        b.Entity<IdentityUserToken<int>>().ToTable("user_token");
        b.Entity<IdentityRoleClaim<int>>().ToTable("role_claim");

        // --- Beziehungen ApplicationUser ---
        b.Entity<ApplicationUser>()
            .HasMany(u => u.Transactions)
            .WithOne(t => t.User)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<ApplicationUser>()
            .HasMany(u => u.GameSessions)
            .WithOne(g => g.User)
            .HasForeignKey(g => g.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<ApplicationUser>()
            .HasMany(u => u.HardwareDeposits)
            .WithOne(h => h.User)
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<ApplicationUser>()
            .HasMany(u => u.CryptoDeposits)
            .WithOne(c => c.User)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<ApplicationUser>()
            .HasMany(u => u.WithdrawalRequests)
            .WithOne(w => w.User)
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Log optional (User kann null sein)
        b.Entity<Log>()
            .HasOne(l => l.User)
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // --- Enum-Konvertierungen (Strings in DB) ---
        b.Entity<Transaction>().Property(t => t.Type).HasConversion<string>();
       
        b.Entity<GameSession>().Property(g => g.GameType).HasConversion<string>();
        b.Entity<GameSession>().Property(g => g.Result).HasConversion<string>();
        b.Entity<Withdrawal>().Property(w => w.Status).HasConversion<string>();
        b.Entity<Log>().Property(l => l.ActionType).HasConversion<string>();
        b.Entity<Log>().Property(l => l.UserType).HasConversion<string>();
    }
}
using CoinDrop;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public class CoinDropContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
{
    public CoinDropContext(DbContextOptions<CoinDropContext> options) : base(options) { }

    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();

    // TPT Root + Derived
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<CryptoDeposit> CryptoDeposits => Set<CryptoDeposit>();
    public DbSet<HardwareDeposit> PhysicalDeposits => Set<HardwareDeposit>();
    public DbSet<Withdrawal> Withdrawals => Set<Withdrawal>();

    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<Log> Logs => Set<Log>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        // Wichtig: Identity zuerst
        base.OnModelCreating(b);

        // ---------- Identity-Tabellen auf lowercase ----------
        b.Entity<ApplicationUser>().ToTable("user");
        b.Entity<IdentityRole<int>>().ToTable("role");
        b.Entity<IdentityUserRole<int>>().ToTable("user_role");
        b.Entity<IdentityUserClaim<int>>().ToTable("user_claim");
        b.Entity<IdentityUserLogin<int>>().ToTable("user_login");
        b.Entity<IdentityUserToken<int>>().ToTable("user_token");
        b.Entity<IdentityRoleClaim<int>>().ToTable("role_claim");

        // ---------- Beziehungen ApplicationUser ----------
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

        // Log optional
        b.Entity<Log>()
            .HasOne(l => l.User)
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<SystemSetting>()
            .ToTable("system_settings");
        // ---------- TPT Mapping ----------
        // Base table
        b.Entity<Transaction>()
            .ToTable("transaction");

        // Derived tables (TPT)
        b.Entity<CryptoDeposit>()
            .ToTable("crypto_deposit");

        b.Entity<HardwareDeposit>()
            .ToTable("physical_deposit");

        b.Entity<Withdrawal>()
            .ToTable("withdrawal_request");

        // Optional: Enum-Konvertierungen (wenn du Strings willst)
        b.Entity<GameSession>().Property(g => g.GameType).HasConversion<string>();
        b.Entity<GameSession>().Property(g => g.Result).HasConversion<string>();
        b.Entity<Withdrawal>().Property(w => w.Status).HasConversion<string>();
        b.Entity<Log>().Property(l => l.ActionType).HasConversion<string>();
        b.Entity<Log>().Property(l => l.UserType).HasConversion<string>();

        // Optional: wenn TransactionType wieder rein soll (empfohlen für Auswertungen)
        // b.Entity<Transaction>().Property(t => t.TransactionType).HasConversion<string>();
    }
}
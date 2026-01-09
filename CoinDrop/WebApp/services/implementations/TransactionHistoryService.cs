using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoinDrop;
using CoinDrop.services.interfaces;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApp.services.dtos;

namespace WebApp.services.implementations;

public sealed class TransactionHistoryService : ITransactionHistoryService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TransactionHistoryService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<PagedResultHistory<TransactionHistoryRowDto>> GetForUserAsync(
        int userId,
        TransactionHistoryQuery query,
        CancellationToken ct = default)
    {
        // ✅ neuer Scope => neue Repo-Instanzen => neuer DbContext => kein Concurrency-Crash
        using var scope = _scopeFactory.CreateScope();

        var txRepo = scope.ServiceProvider.GetRequiredService<TransactionRepo>();
        var hRepo = scope.ServiceProvider.GetRequiredService<HDepositRepo>();
        var cRepo = scope.ServiceProvider.GetRequiredService<CDepositRepo>();
        var wRepo = scope.ServiceProvider.GetRequiredService<WithdrawalRepo>();

        IQueryable<Transaction> baseTx = txRepo.Query()
            .AsNoTracking()
            .Where(t => t.UserId == userId);

        // Action filter
        if (query.Action == TransactionActionFilter.Deposit)
            baseTx = baseTx.Where(t => t is HardwareDeposit || t is CryptoDeposit);
        else if (query.Action == TransactionActionFilter.Withdrawal)
            baseTx = baseTx.Where(t => t is Withdrawal);

        // Type filter
        if (query.Type == TransactionTypeFilter.Physical)
            baseTx = baseTx.Where(t => t is HardwareDeposit);
        else if (query.Type == TransactionTypeFilter.Crypto)
            baseTx = baseTx.Where(t => t is CryptoDeposit || t is Withdrawal);

        // Date range (UTC)
        if (query.FromUtc.HasValue)
            baseTx = baseTx.Where(t => t.Timestamp >= query.FromUtc.Value);

        if (query.ToUtc.HasValue)
            baseTx = baseTx.Where(t => t.Timestamp <= query.ToUtc.Value);

        // ✅ MinDepositEur (nur auf Deposits)
        if (query.MinDepositEur.HasValue)
        {
            var min = query.MinDepositEur.Value;
            baseTx = baseTx.Where(t =>
                !(t is HardwareDeposit || t is CryptoDeposit) || t.EurAmount >= min
            );
        }

        var hardware = hRepo.Query().AsNoTracking();
        var cryptoDep = cRepo.Query().AsNoTracking();
        var withdrawal = wRepo.Query().AsNoTracking();

        var projected =
            from t in baseTx
            join hd in hardware on t.TransactionId equals hd.TransactionId into hdj
            from hd in hdj.DefaultIfEmpty()

            join cd in cryptoDep on t.TransactionId equals cd.TransactionId into cdj
            from cd in cdj.DefaultIfEmpty()

            join wd in withdrawal on t.TransactionId equals wd.TransactionId into wdj
            from wd in wdj.DefaultIfEmpty()

            select new TransactionHistoryRowDto
            {
                Action = wd != null ? "Withdrawal" : "Deposit",
                Type = (hd != null) ? "Physical" : "Crypto",
                EurAmount = t.EurAmount,
                AssetType = (hd != null) ? "Cash" : (wd != null ? wd.Asset : (cd != null ? cd.Asset : "")),
                Network = (hd != null) ? "" : (cd != null ? cd.Network : ""),
                TimestampUtc = t.Timestamp,
                TxHash = wd != null ? wd.TxHash : (cd != null ? cd.TxHash : null)
            };

        projected = projected.OrderByDescending(x => x.TimestampUtc);

        var items = await projected.ToArrayAsync(ct);

        return new PagedResultHistory<TransactionHistoryRowDto>
        {
            Items = items,
            TotalCount = items.Length,
            Page = 1,
            PageSize = items.Length
        };
    }
}
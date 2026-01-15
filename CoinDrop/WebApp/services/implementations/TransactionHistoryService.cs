using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoinDrop;
using CoinDrop.services.interfaces;
using Domain;
using Microsoft.EntityFrameworkCore;
using WebApp.services.dtos;

namespace WebApp.services.implementations;

public sealed class TransactionHistoryService : ITransactionHistoryService
{
    private readonly TransactionRepo _txRepo;
    private readonly HDepositRepo _hRepo;
    private readonly CDepositRepo _cRepo;
    private readonly WithdrawalRepo _wRepo;

    // ✅ Direkte Injection der benötigten Repositories
    public TransactionHistoryService(
        TransactionRepo txRepo,
        HDepositRepo hRepo,
        CDepositRepo cRepo,
        WithdrawalRepo wRepo)
    {
        _txRepo = txRepo;
        _hRepo = hRepo;
        _cRepo = cRepo;
        _wRepo = wRepo;
    }

    public async Task<PagedResultHistory<TransactionHistoryRowDto>> GetForUserAsync(
        int userId,
        TransactionHistoryQuery query,
        CancellationToken ct = default)
    {
        // ✅ Alle Daten asynchron mit ExecuteQueryAsync laden
        var transactions = await _txRepo.ExecuteQueryAsync(
            queryBuilder: q => q
                .AsNoTracking()
                .Where(t => t.UserId == userId),
            ct
        );

        var hardware = await _hRepo.ExecuteQueryAsync(
            q => q.AsNoTracking(),
            ct
        );

        var cryptoDep = await _cRepo.ExecuteQueryAsync(
            q => q.AsNoTracking(),
            ct
        );

        var withdrawals = await _wRepo.ExecuteQueryAsync(
            q => q.AsNoTracking(),
            ct
        );

        // ✅ In-Memory Filter anwenden (da ExecuteQueryAsync Listen zurückgibt)
        var filteredTransactions = transactions.AsEnumerable();

        // Action filter
        if (query.Action == TransactionActionFilter.Deposit)
            filteredTransactions = filteredTransactions
                .Where(t => t is HardwareDeposit || t is CryptoDeposit);
        else if (query.Action == TransactionActionFilter.Withdrawal)
            filteredTransactions = filteredTransactions
                .Where(t => t is Withdrawal);

        // Type filter
        if (query.Type == TransactionTypeFilter.Physical)
            filteredTransactions = filteredTransactions
                .Where(t => t is HardwareDeposit);
        else if (query.Type == TransactionTypeFilter.Crypto)
            filteredTransactions = filteredTransactions
                .Where(t => t is CryptoDeposit || t is Withdrawal);

        // Date range (UTC)
        if (query.FromUtc.HasValue)
            filteredTransactions = filteredTransactions
                .Where(t => t.Timestamp >= query.FromUtc.Value);

        if (query.ToUtc.HasValue)
            filteredTransactions = filteredTransactions
                .Where(t => t.Timestamp <= query.ToUtc.Value);

        // ✅ MinDepositEur (nur auf Deposits)
        if (query.MinDepositEur.HasValue)
        {
            var min = query.MinDepositEur.Value;
            filteredTransactions = filteredTransactions
                .Where(t => !(t is HardwareDeposit || t is CryptoDeposit) || t.EurAmount >= min);
        }

        // ✅ In-Memory Join durchführen
        var result = filteredTransactions
            .Select(t =>
            {
                var hd = hardware.FirstOrDefault(h => h.TransactionId == t.TransactionId);
                var cd = cryptoDep.FirstOrDefault(c => c.TransactionId == t.TransactionId);
                var wd = withdrawals.FirstOrDefault(w => w.TransactionId == t.TransactionId);

                return new TransactionHistoryRowDto
                {
                    Action = wd != null ? "Withdrawal" : "Deposit",
                    Type = hd != null ? "Physical" : "Crypto",
                    EurAmount = t.EurAmount,
                    AssetType = hd != null ? "Cash" : (wd != null ? wd.Asset : (cd != null ? cd.Asset : "")),
                    Network = hd != null ? "" : (cd != null ? cd.Network : ""),
                    TimestampUtc = t.Timestamp,
                    TxHash = wd != null ? wd.TxHash : (cd != null ? cd.TxHash : null)
                };
            })
            .OrderByDescending(x => x.TimestampUtc)
            .ToArray();

        return new PagedResultHistory<TransactionHistoryRowDto>
        {
            Items = result,
            TotalCount = result.Length,
            Page = 1,
            PageSize = result.Length
        };
    }
}
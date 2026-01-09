using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CoinDrop;
using Domain;
using CoinDrop.services.interfaces;
using WebApp.services.dtos;

namespace WebApp.services.implementations;

public sealed class AdminDashboardService : IAdminDashboardService
{
    private readonly IRepository<GameSession> _gameSessions;
    private readonly IRepository<CryptoDeposit> _cryptoDeposits;
    private readonly IRepository<HardwareDeposit> _hardwareDeposits;
    private readonly IRepository<Withdrawal> _withdrawals;
    private readonly IRepository<Log> _logs;

    public AdminDashboardService(
        IRepository<GameSession> gameSessions,
        IRepository<CryptoDeposit> cryptoDeposits,
        IRepository<HardwareDeposit> hardwareDeposits,
        IRepository<Withdrawal> withdrawals,
        IRepository<Log> logs)
    {
        _gameSessions = gameSessions;
        _cryptoDeposits = cryptoDeposits;
        _hardwareDeposits = hardwareDeposits;
        _withdrawals = withdrawals;
        _logs = logs;
    }

    public async Task<DashboardStatsDto> GetStatsAsync(TimeRange range, CancellationToken ct = default)
    {
        // =========================
        // ALL TIME (nur dieses Feld)
        // =========================
        var gsAll = _gameSessions.Query().AsNoTracking();

        var casinoAllWonSum = await gsAll
            .Where(x => x.Result == GameResult.Loss)          // User Loss => Casino gewinnt Bet
            .SumAsync(x => (double?)x.BetAmount, ct) ?? 0.0;

        var casinoAllLostSum = await gsAll
            .Where(x => x.Result == GameResult.Win)           // User Win => Casino verliert WinAmount
            .SumAsync(x => (double?)x.WinAmount, ct) ?? 0.0;

        var casinoNetAllTime = casinoAllWonSum - casinoAllLostSum;

        // =========================
        // RANGE (LOCAL TIME FIX)
        // =========================
        var (from, to) = GetRangeLocal(range);

        var gsRange = _gameSessions.Query()
            .AsNoTracking()
            .Where(x => x.Timestamp >= from && x.Timestamp < to);

        var casinoWonCount = await gsRange.CountAsync(x => x.Result == GameResult.Loss, ct);
        var casinoLostCount = await gsRange.CountAsync(x => x.Result == GameResult.Win, ct);
        var drawCount = await gsRange.CountAsync(x => x.Result == GameResult.Draw, ct);

        var rangeCasinoWonSum = await gsRange
            .Where(x => x.Result == GameResult.Loss)
            .SumAsync(x => (double?)x.BetAmount, ct) ?? 0.0;

        var rangeCasinoLostSum = await gsRange
            .Where(x => x.Result == GameResult.Win)
            .SumAsync(x => (double?)x.WinAmount, ct) ?? 0.0;

        // ✅ WinOrLose im Range
        var winOrLose = rangeCasinoWonSum - rangeCasinoLostSum;

        var highestBet = await gsRange.MaxAsync(x => (double?)x.BetAmount, ct) ?? 0.0;

        // Deposits/Withdrawals RANGE (LOCAL TIME FIX)
        var cdSum = await _cryptoDeposits.Query().AsNoTracking()
            .Where(x => x.Timestamp >= from && x.Timestamp < to)
            .SumAsync(x => (double?)x.EurAmount, ct) ?? 0.0;

        var hdSum = await _hardwareDeposits.Query().AsNoTracking()
            .Where(x => x.Timestamp >= from && x.Timestamp < to)
            .SumAsync(x => (double?)x.CoinValue, ct) ?? 0.0;

        var wdSum = await _withdrawals.Query().AsNoTracking()
            .Where(x => x.Timestamp >= from && x.Timestamp < to)
            .SumAsync(x => (double?)x.EurAmount, ct) ?? 0.0;

        var depositsTotal = cdSum + hdSum;
        var depositsMinusWithdrawals = depositsTotal - wdSum;

        // RANGE Total Formula:
        // Einzahlungen + Gewinne - Auszahlungen + Verluste
        // Gewinne (Casino gewinnt) = rangeCasinoWonSum
        // Verluste (Casino verliert) = rangeCasinoLostSum
        var rangeTotalFormula = (depositsTotal) - wdSum;

        return new DashboardStatsDto
        {
            CasinoNetFromGames = casinoNetAllTime,

            WinOrLose = winOrLose,

            CasinoGamesWon = casinoWonCount,
            CasinoGamesLost = casinoLostCount,
            GamesDraw = drawCount,

            DepositsMinusWithdrawals = depositsMinusWithdrawals,
            WithdrawalsSum = wdSum,
            CryptoDepositsSum = cdSum,
            HardwareDepositsSum = hdSum,

            HighestBet = highestBet,

            RangeTotalFormula = rangeTotalFormula
        };
    }

    public async Task<PagedResultAdmin<LogItemDto>> GetLogsAsync(
        TimeRange range,
        int pageIndex,
        int pageSize,
        CancellationToken ct = default)
    {
        // ✅ LOCAL TIME FIX (damit MySQL datetime ohne TZ passt)
        var (from, to) = GetRangeLocal(range);

        var query = _logs.Query()
            .AsNoTracking()
            .Where(x => x.Date >= from && x.Date < to)
            .OrderByDescending(x => x.Date);

        var items = await query
            .Skip(pageIndex * pageSize)
            .Take(pageSize + 1)
            .Select(x => new LogItemDto
            {
                LogId = x.LogId,
                Date = x.Date,
                ActionType = x.ActionType.ToString(),
                UserType = x.UserType.ToString(),
                UserId = x.UserId,
                Description = x.Description
            })
            .ToListAsync(ct);

        var hasMore = items.Count > pageSize;
        if (hasMore)
            items.RemoveAt(items.Count - 1);

        return new PagedResultAdmin<LogItemDto>
        {
            Items = items,
            HasMore = hasMore
        };
    }

    /// <summary>
    /// MySQL "datetime" hat keine Zeitzone. Wenn DB-Werte lokal gespeichert werden,
    /// muss auch lokal gefiltert werden (DateTime.Now) statt UTC, sonst fallen Daten aus dem Fenster.
    /// </summary>
    private static (DateTime from, DateTime to) GetRangeLocal(TimeRange range)
    {
        var now = DateTime.Now; // ✅ lokal
        var todayFrom = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0); // Kind: Unspecified

        return range switch
        {
            TimeRange.Today => (todayFrom, todayFrom.AddDays(1)),
            TimeRange.LastWeek => (todayFrom.AddDays(-7), todayFrom.AddDays(1)),
            TimeRange.LastMonth => (todayFrom.AddMonths(-1), todayFrom.AddDays(1)),
            TimeRange.LastYear => (todayFrom.AddYears(-1), todayFrom.AddDays(1)),
            _ => (todayFrom, todayFrom.AddDays(1))
        };
    }
}
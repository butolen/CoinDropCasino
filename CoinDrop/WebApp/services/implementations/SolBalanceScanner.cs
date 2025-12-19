using CoinDrop.config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Solnet.Rpc;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Types;
using Solnet.Wallet;
using Solnet.Programs;

public class SolBalanceScanner : BackgroundService
{
    private readonly IDbContextFactory<CoinDropContext> _dbFactory;
    private readonly IRpcClient _rpc;
    private readonly CryptoConfig _cfg;

    private const int MaxParallel = 10;
    private static readonly TimeSpan LoopDelay = TimeSpan.FromSeconds(10);

    public SolBalanceScanner(
        IDbContextFactory<CoinDropContext> dbFactory,
        IOptions<CryptoConfig> cfg)
    {
        _dbFactory = dbFactory;
        _cfg = cfg.Value;

        _rpc = ClientFactory.GetClient(
            _cfg.Cluster == "MainNet" ? Cluster.MainNet : Cluster.DevNet);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await ScanAllAsync(ct);
            await Task.Delay(LoopDelay, ct);
        }
    }

    private async Task ScanAllAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.DepositAddress != null && u.DepositAddress != "")
            .Select(u => new { u.Id, DepositAddress = u.DepositAddress! })
            .ToListAsync(ct);

        if (users.Count == 0)
            return;

        using var sem = new SemaphoreSlim(MaxParallel);

        var tasks = users.Select(async u =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var bal = await _rpc.GetBalanceAsync(u.DepositAddress, Commitment.Finalized);

                // Balance-Check (wie gewünscht minimal)
                if (!bal.WasSuccessful || bal.Result.Value <= 10_000_000UL)
                {
                    if (bal.WasSuccessful)
                        Console.WriteLine(bal.Result.Value);

                    return;
                }

                Account depositAccount = GetDepositAccountFromUserId(u.Id);

                if (depositAccount.PublicKey != u.DepositAddress)
                    return;

                var sig = await SweepSolAsync(_rpc, depositAccount, _cfg.TreasuryAddress, ct);

                if (sig != null)
                    Console.WriteLine($"[SWEEP_OK] userId={u.Id} {u.DepositAddress} -> {_cfg.TreasuryAddress} sig={sig}");
                else
                    Console.WriteLine($"[SWEEP_SKIP/FAIL] userId={u.Id} addr={u.DepositAddress}");
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private Account GetDepositAccountFromUserId(int userId)
    {
        var wallet = new Wallet(_cfg.MasterMnemonic);
        return wallet.GetAccount(userId);
    }

    private async Task<string?> SweepSolAsync(
        IRpcClient rpc,
        Account depositAccount,
        string treasuryAddress,
        CancellationToken ct)
    {
        var balanceRes = await rpc.GetBalanceAsync(depositAccount.PublicKey, Commitment.Finalized);
        if (!balanceRes.WasSuccessful)
            return null;

        ulong balanceLamports = balanceRes.Result.Value;
        if (balanceLamports == 0)
            return null;

        var rentRes = await rpc.GetMinimumBalanceForRentExemptionAsync(0, Commitment.Finalized);
        if (!rentRes.WasSuccessful)
            return null;

        ulong rentMin = rentRes.Result;
        const ulong feeReserveLamports = 10_000UL;

        if (balanceLamports <= rentMin + feeReserveLamports)
            return null;

        ulong sweepLamports = balanceLamports - rentMin - feeReserveLamports;

        var bh = await rpc.GetLatestBlockHashAsync(Commitment.Finalized);
        if (!bh.WasSuccessful)
            return null;

        PublicKey treasuryPubKey = new PublicKey(treasuryAddress);

        var tx = new TransactionBuilder()
            .SetRecentBlockHash(bh.Result.Value.Blockhash)
            .SetFeePayer(depositAccount)
            .AddInstruction(SystemProgram.Transfer(
                fromPublicKey: depositAccount.PublicKey,
                toPublicKey: treasuryPubKey,
                lamports: sweepLamports))
            .Build(depositAccount);

        var sendRes = await rpc.SendTransactionAsync(tx, skipPreflight: false, Commitment.Finalized);
        if (!sendRes.WasSuccessful)
            return null;

        string sig = sendRes.Result;

        // OPTION A: auf Finalized warten, damit der nächste Loop nicht nochmal sweeped
        bool finalizedOk = await WaitForFinalizedAsync(rpc, sig, ct);
        if (!finalizedOk)
            return null;

        return sig;
    }

    private static async Task<bool> WaitForFinalizedAsync(IRpcClient rpc, string signature, CancellationToken ct)
    {
        // Devnet kann laggen → lieber etwas Puffer
        TimeSpan timeout = TimeSpan.FromSeconds(60);
        TimeSpan delay = TimeSpan.FromSeconds(2);
        DateTime start = DateTime.UtcNow;

        while (!ct.IsCancellationRequested && DateTime.UtcNow - start < timeout)
        {
            // Solnet: Status abfragen
            var st = await rpc.GetSignatureStatusesAsync(
                new List<string> { signature },
                searchTransactionHistory: true);
            if (st.WasSuccessful && st.Result?.Value != null && st.Result.Value.Count > 0)
            {
                var s0 = st.Result.Value[0];
                if (s0 != null)
                {
                    // Wenn Fehler drin steht -> failed
                    if (s0.Error != null)
                        return false;

                    // ConfirmationStatus ist je nach Solnet-Version string/enum → ToString() ist safe
                    var status = s0.ConfirmationStatus?.ToString();
                    if (string.Equals(status, "finalized", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            await Task.Delay(delay, ct);
        }

        return false; // Timeout oder abgebrochen
    }
}
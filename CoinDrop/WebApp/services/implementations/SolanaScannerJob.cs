using CoinDrop.config;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Solnet.Programs;
using Solnet.Rpc;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Types;
using Solnet.Wallet;
using System.Net.Http.Json;

namespace CoinDrop.services;

public sealed class SolBalanceScannerJob
{
    private readonly IDbContextFactory<CoinDropContext> _dbFactory;
    private readonly IRpcClient _rpc;
    private readonly CryptoConfig _cfg;

    private readonly CDepositRepo _cDepositRepo;
    private readonly LogRepo _logRepo;

    private const int MaxParallel = 10;

    // Business: "fees treasury zahlt" -> wir bauen TX mit fee payer treasury
    private const ulong FeeReserveLamports = 10_000UL;

    // Sweep-Trigger: wie bei dir (>= 0.01 SOL)
    private const ulong MinTriggerLamports = 10_000_000UL;

    // CoinGecko: Public (ohne API-Key)
    private static readonly HttpClient _http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public SolBalanceScannerJob(
        IDbContextFactory<CoinDropContext> dbFactory,
        IOptions<CryptoConfig> cfg,
        CDepositRepo cDepositRepo,
        LogRepo logRepo)
    {
        _dbFactory = dbFactory;
        _cfg = cfg.Value;
        _cDepositRepo = cDepositRepo;
        _logRepo = logRepo;

        _rpc = ClientFactory.GetClient(
            _cfg.Cluster == "MainNet" ? Cluster.MainNet : Cluster.DevNet);
    }

    public async Task RunOnceAsync(CancellationToken ct)
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
                await HandleUserAsync(u.Id, u.DepositAddress, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SOL_SCAN_USER_ERR] userId={u.Id} {ex}");
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task HandleUserAsync(int userId, string depositAddress, CancellationToken ct)
    {
        // 1) Balance check (minimal)
        var bal = await _rpc.GetBalanceAsync(depositAddress, Commitment.Finalized);
        if (!bal.WasSuccessful || bal.Result.Value <= MinTriggerLamports)
            return;

        // 2) Inbound Transfer finden -> SourceAddress + inboundSig + inboundLamports
        var inbound = await TryGetLatestInboundTransferAsync(depositAddress, ct);
        if (inbound == null)
            return;

        // 3) Safety: DepositAccount muss deterministisch zum User passen
        Account depositAccount = GetDepositAccountFromUserId(userId);
        if (!string.Equals(depositAccount.PublicKey, depositAddress, StringComparison.Ordinal))
            return;

        // 4) Duplikate verhindern: TxHash = inbound signature (Deposit Tx)
        bool exists = await _cDepositRepo.Query()
            .AsNoTracking()
            .AnyAsync(d => d.TxHash == inbound.Value.InboundSignature, ct);

        if (exists)
            return;

        // 5) Sweep -> FeePayer = Treasury (Fees zahlt Treasury), Signer: Treasury + Deposit
        var sweep = await SweepSolAsyncPayFeeByTreasuryAsync(
            rpc: _rpc,
            depositAccount: depositAccount,
            treasuryMnemonic: _cfg.TreasuryMnemonic,
            treasuryAddress: _cfg.TreasuryAddress,
            ct: ct);

        if (sweep == null)
            return;

        // 6) Deposit Amount berechnen (wie vorher)
        ulong grossLamports = inbound.Value.InboundLamports;

        ulong netLamports = grossLamports > FeeReserveLamports
            ? grossLamports - FeeReserveLamports
            : grossLamports;

        double netSol = netLamports / 1_000_000_000d;

        // ✅ 6.1) CoinGecko Preis (SOL->EUR) holen (ohne API key)
        double? solPriceEur = await GetSolPriceEurAsync(ct);

        // ✅ 6.2) EUR Amount berechnen (BalanceCrypto soll EUR sein)
        double eurAmount = (solPriceEur.HasValue && solPriceEur.Value > 0.0)
            ? netSol * solPriceEur.Value
            : 0.0;

        // 7) Persist: CryptoDeposit (TPT -> transaction + crypto_deposit)
        var deposit = new CryptoDeposit
        {
            UserId = userId,

            // Base Transaction
            EurAmount = eurAmount,
            Timestamp = DateTime.UtcNow,
            Details =
                $"InboundSig={inbound.Value.InboundSignature}; " +
                $"SweepSig={sweep.Value.SweepSignature}; " +
                $"grossLamports={grossLamports}; netLamports={netLamports}; " +
                $"feeReserveLamports={FeeReserveLamports}; " +
                $"treasury={_cfg.TreasuryAddress}; " +
                $"solPriceEur={(solPriceEur?.ToString() ?? "null")}",

            // Child CryptoDeposit
            Network = "Solana",
            DepositAddress = depositAddress,
            SourceAddress = inbound.Value.SourceAddress, // echte Senderwallet
            Asset = "SOL",
            Amount = netSol,
            TxHash = inbound.Value.InboundSignature       // Deposit TxHash
        };

        await _cDepositRepo.AddAsync(deposit, ct);

        // ✅ 7.1) User-Balance updaten (user.balancecrypto += eurAmount)
        // Extra DbContext, tracked Entity
        if (eurAmount > 0.0)
        {
            await using var db2 = await _dbFactory.CreateDbContextAsync(ct);
            var dbUser = await db2.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

            if (dbUser != null)
            {
                dbUser.BalanceCrypto += eurAmount;
                await db2.SaveChangesAsync(ct);
            }
        }

        // 8) Log
        var log = new Log
        {
            ActionType = LogActionType.UserAction,
            UserType = LogUserType.User,
            UserId = userId,
            Description =
                "SOL deposit detected, swept to treasury (fees paid by treasury), persisted, and user balance updated. " +
                $"userId={userId}; " +
                $"cluster={_cfg.Cluster}; " +
                $"depositAddress={depositAddress}; " +
                $"sourceAddress={inbound.Value.SourceAddress}; " +
                $"asset=SOL; " +
                $"grossLamports={grossLamports}; " +
                $"netLamports={netLamports}; " +
                $"netSol={netSol}; " +
                $"solPriceEur={(solPriceEur?.ToString() ?? "null")}; " +
                $"eurAmount={eurAmount}; " +
                $"feeReserveLamports={FeeReserveLamports}; " +
                $"inboundSig={inbound.Value.InboundSignature}; " +
                $"sweepSig={sweep.Value.SweepSignature}; " +
                $"treasuryAddress={_cfg.TreasuryAddress}; " +
                $"tsUtc={DateTime.UtcNow:O}"
        };

        await _logRepo.AddAsync(log, ct);

        Console.WriteLine(
            $"[DEPOSIT_OK] userId={userId} netSol={netSol} eurAmount={eurAmount} " +
            $"inbound={inbound.Value.InboundSignature} sweep={sweep.Value.SweepSignature}");
    }

    private Account GetDepositAccountFromUserId(int userId)
    {
        var wallet = new Wallet(_cfg.MasterMnemonic);
        return wallet.GetAccount(userId);
    }

    private async Task<(string SweepSignature, ulong SweptLamports)?> SweepSolAsyncPayFeeByTreasuryAsync(
        IRpcClient rpc,
        Account depositAccount,
        string treasuryMnemonic,
        string treasuryAddress,
        CancellationToken ct)
    {
        // Treasury signer (FeePayer)
        var treasuryWallet = new Wallet(treasuryMnemonic);
        Account treasuryAccount = treasuryWallet.GetAccount(0);

        // Config sanity check
        if (!string.Equals(treasuryAccount.PublicKey, treasuryAddress, StringComparison.Ordinal))
        {
            Console.WriteLine("[SWEEP_CFG_ERR] TreasuryMnemonic passt nicht zur TreasuryAddress!");
            return null;
        }

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

        if (balanceLamports <= rentMin + FeeReserveLamports)
            return null;

        // NET Sweep
        ulong sweepLamports = balanceLamports - rentMin - FeeReserveLamports;

        var bh = await rpc.GetLatestBlockHashAsync(Commitment.Finalized);
        if (!bh.WasSuccessful)
            return null;

        var treasuryPubKey = new Solnet.Wallet.PublicKey(treasuryAddress);

        var tx = new TransactionBuilder()
            .SetRecentBlockHash(bh.Result.Value.Blockhash)
            .SetFeePayer(treasuryAccount) // Treasury zahlt Fee
            .AddInstruction(SystemProgram.Transfer(
                fromPublicKey: depositAccount.PublicKey,
                toPublicKey: treasuryPubKey,
                lamports: sweepLamports))
            .Build(new List<Account> { treasuryAccount, depositAccount });

        var sendRes = await rpc.SendTransactionAsync(tx, skipPreflight: false, Commitment.Finalized);
        if (!sendRes.WasSuccessful)
            return null;

        string sig = sendRes.Result;

        bool finalizedOk = await WaitForFinalizedAsync(rpc, sig, ct);
        if (!finalizedOk)
            return null;

        return (sig, sweepLamports);
    }

    private async Task<(string SourceAddress, string InboundSignature, ulong InboundLamports)?>
        TryGetLatestInboundTransferAsync(string depositAddress, CancellationToken ct)
    {
        var sigs = await _rpc.GetSignaturesForAddressAsync(depositAddress, limit: 20, commitment: Commitment.Finalized);
        if (!sigs.WasSuccessful || sigs.Result == null || sigs.Result.Count == 0)
            return null;

        foreach (var s in sigs.Result)
        {
            ct.ThrowIfCancellationRequested();

            var txRes = await _rpc.GetTransactionAsync(s.Signature, Commitment.Finalized);
            if (!txRes.WasSuccessful || txRes.Result?.Transaction?.Message == null)
                continue;

            var msg = txRes.Result.Transaction.Message;
            var meta = txRes.Result.Meta;

            if (meta?.PreBalances == null || meta.PostBalances == null)
                continue;

            int depositIndex = -1;
            int keyCount = msg.AccountKeys.Length;

            for (int i = 0; i < keyCount; i++)
            {
                if (string.Equals(msg.AccountKeys[i], depositAddress, StringComparison.Ordinal))
                {
                    depositIndex = i;
                    break;
                }
            }

            if (depositIndex < 0)
                continue;

            long deltaDeposit =
                (long)meta.PostBalances[depositIndex] -
                (long)meta.PreBalances[depositIndex];

            if (deltaDeposit <= 0)
                continue;

            int balanceCount = Math.Min(meta.PreBalances.Length, meta.PostBalances.Length);

            int sourceIndex = -1;
            long mostNegative = 0;

            for (int i = 0; i < balanceCount; i++)
            {
                long delta =
                    (long)meta.PostBalances[i] -
                    (long)meta.PreBalances[i];

                if (delta < mostNegative)
                {
                    mostNegative = delta;
                    sourceIndex = i;
                }
            }

            if (sourceIndex < 0)
                continue;

            string sourceAddress = msg.AccountKeys[sourceIndex];
            ulong inboundLamports = (ulong)deltaDeposit;

            return (sourceAddress, s.Signature, inboundLamports);
        }

        return null;
    }

    private static async Task<bool> WaitForFinalizedAsync(IRpcClient rpc, string signature, CancellationToken ct)
    {
        TimeSpan timeout = TimeSpan.FromSeconds(60);
        TimeSpan delay = TimeSpan.FromSeconds(2);
        DateTime start = DateTime.UtcNow;

        while (!ct.IsCancellationRequested && DateTime.UtcNow - start < timeout)
        {
            var st = await rpc.GetSignatureStatusesAsync(
                new List<string> { signature },
                searchTransactionHistory: true);

            if (st.WasSuccessful && st.Result?.Value != null && st.Result.Value.Count > 0)
            {
                var s0 = st.Result.Value[0];
                if (s0 != null)
                {
                    if (s0.Error != null)
                        return false;

                    var status = s0.ConfirmationStatus?.ToString();
                    if (string.Equals(status, "finalized", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            await Task.Delay(delay, ct);
        }

        return false;
    }

    // ✅ CoinGecko Public Price: SOL → EUR (ohne API Key)
    private static async Task<double?> GetSolPriceEurAsync(CancellationToken ct)
    {
        const string url = "https://api.coingecko.com/api/v3/simple/price?ids=solana&vs_currencies=eur";

        using var res = await _http.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode)
            return null;

        var json = await res.Content.ReadFromJsonAsync<Dictionary<string, Dictionary<string, double>>>(cancellationToken: ct);
        if (json == null)
            return null;

        if (!json.TryGetValue("solana", out var inner))
            return null;

        return inner.TryGetValue("eur", out var price)
            ? price
            : null;
    }
}
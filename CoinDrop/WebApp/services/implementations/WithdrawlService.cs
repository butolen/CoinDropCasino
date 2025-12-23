using System.Net.Http.Json;
using CoinDrop.config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Solnet.Programs;
using Solnet.Rpc;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Types;
using Solnet.Wallet;

namespace WebApp.services.implementations;

public sealed class WithdrawlService
{
    private readonly HttpClient _http;
    private readonly IDbContextFactory<CoinDropContext> _dbFactory;
    private readonly CryptoConfig _cfg;
    private readonly IRpcClient _rpc;

    // App-intern: wir rechnen eine Fix-Fee (Lamports) als "User pays fee" in EUR ab
    // On-chain zahlt Treasury trotzdem die Fee.
    private const ulong AppFeeLamports = 10_000UL; // ~0.00001 SOL (anpassbar)

    public WithdrawlService(
        HttpClient http,
        IDbContextFactory<CoinDropContext> dbFactory,
        IOptions<CryptoConfig> cfg)
    {
        _http = http;
        _dbFactory = dbFactory;
        _cfg = cfg.Value;

        _rpc = Solnet.Rpc.ClientFactory.GetClient(
            _cfg.Cluster == "MainNet" ? Solnet.Rpc.Cluster.MainNet : Solnet.Rpc.Cluster.DevNet);
    }

    public async Task<double?> GetSolPriceEurAsync(CancellationToken ct)
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

        return inner.TryGetValue("eur", out var price) ? price : null;
    }

    /// <summary>
    /// Überweist SOL von Treasury zur Zieladresse und reduziert die User BalanceCrypto (EUR).
    /// Returns: sweepSignature (tx sig)
    /// </summary>
    public async Task<string> WithdrawAsync(int userId, string withdrawalAddress, double amountSol, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(withdrawalAddress))
            throw new ArgumentException("Withdrawal address is required.", nameof(withdrawalAddress));

        if (amountSol <= 0.0)
            throw new ArgumentException("Amount must be > 0.", nameof(amountSol));

        // 1) SOL Preis holen
        var solPriceEur = await GetSolPriceEurAsync(ct);
        if (!solPriceEur.HasValue || solPriceEur.Value <= 0.0)
            throw new InvalidOperationException("SOL price unavailable (CoinGecko). Try again.");

        double amountEur = amountSol * solPriceEur.Value;

        // "Fees werden von Treasury gezahlt" (on-chain),
        // "aber vom User Balance abziehen" (app-intern):
        double feeSol = AppFeeLamports / 1_000_000_000d;
        double feeEur = feeSol * solPriceEur.Value;

        double totalDebitEur = amountEur + feeEur;

        // 2) DB: Balance check + reservieren (transactional)
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Tracking an
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null)
            throw new InvalidOperationException("User not found.");

        if (user.BalanceCrypto < totalDebitEur)
            throw new InvalidOperationException($"Insufficient balance. Needed {totalDebitEur:0.00} EUR.");

        // 3) Treasury Wallet laden + sanity check
        var treasuryWallet = new Wallet(_cfg.TreasuryMnemonic);
        var treasuryAccount = treasuryWallet.GetAccount(0);

        if (!string.Equals(treasuryAccount.PublicKey, _cfg.TreasuryAddress, StringComparison.Ordinal))
            throw new InvalidOperationException("TreasuryMnemonic does not match TreasuryAddress.");

        // 4) On-chain: Treasury Balance check (SOL)
        var treasuryBal = await _rpc.GetBalanceAsync(treasuryAccount.PublicKey, Commitment.Finalized);
        if (!treasuryBal.WasSuccessful)
            throw new InvalidOperationException("Failed to read treasury balance.");

        ulong needLamports = (ulong)Math.Ceiling(amountSol * 1_000_000_000d);

        // On-chain fee zahlt Treasury sowieso, aber wir checken grob: amount + ein kleiner buffer
        if (treasuryBal.Result.Value < needLamports + AppFeeLamports)
            throw new InvalidOperationException("Treasury has insufficient SOL to execute withdrawal.");

        // 5) TX bauen: FeePayer = Treasury, From = Treasury, To = withdrawalAddress
        var bh = await _rpc.GetLatestBlockHashAsync(Commitment.Finalized);
        if (!bh.WasSuccessful)
            throw new InvalidOperationException("Failed to get latest blockhash.");

        var toPubKey = new Solnet.Wallet.PublicKey(withdrawalAddress);

        var tx = new TransactionBuilder()
            .SetRecentBlockHash(bh.Result.Value.Blockhash)
            .SetFeePayer(treasuryAccount) // ✅ fees paid by treasury
            .AddInstruction(SystemProgram.Transfer(
                fromPublicKey: treasuryAccount.PublicKey,
                toPublicKey: toPubKey,
                lamports: needLamports))
            .Build(new List<Account> { treasuryAccount }); // ✅ only treasury signs

        var send = await _rpc.SendTransactionAsync(tx, skipPreflight: false, Commitment.Finalized);
        if (!send.WasSuccessful || string.IsNullOrWhiteSpace(send.Result))
            throw new InvalidOperationException("Withdraw transaction failed to send.");

        string signature = send.Result;

        // 6) Finalize warten (damit wir nur dann abbuchen, wenn TX wirklich final ist)
        bool finalized = await WaitForFinalizedAsync(_rpc, signature, ct);
        if (!finalized)
            throw new InvalidOperationException("Withdraw tx not finalized (timeout / error).");

        // 7) Jetzt erst: User Balance abziehen (inkl. Fee) + speichern
        user.BalanceCrypto -= totalDebitEur;

        // OPTIONAL: hier Withdrawal-Entity speichern (wenn du eine hast)
        // db.CryptoWithdrawals.Add(new CryptoWithdrawal{ ... Signature=signature, AmountSol=amountSol, EurAmount=amountEur, FeeEur=feeEur ... });

        await db.SaveChangesAsync(ct);

        return signature;
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
}
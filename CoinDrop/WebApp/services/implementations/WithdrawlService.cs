using CoinDrop;
using CoinDrop.config;
using CoinDrop.services.interfaces;
using Domain;
using Microsoft.Extensions.Options;
using Solnet.Programs;
using Solnet.Rpc;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Types;
using Solnet.Wallet;
using WebApp.services.implementations;

public sealed class WithdrawlService
{
    private readonly IPriceService _priceService;
    private readonly IRepository<ApplicationUser> _userRepository;
    private readonly IRepository<Withdrawal> _withdrawalRepository;
    private readonly IRepository<Log> _logRepository;
    private readonly ISystemSettingsService _settingsService;
    private readonly CryptoConfig _cfg;
    private readonly IRpcClient _rpc;

    public WithdrawlService(
        IPriceService priceService,
        IRepository<ApplicationUser> userRepository,
        IRepository<Withdrawal> withdrawalRepository,
        IRepository<Log> logRepository,
        ISystemSettingsService settingsService,
        IOptions<CryptoConfig> cfg)
    {
        _priceService = priceService;
        _userRepository = userRepository;
        _withdrawalRepository = withdrawalRepository;
        _logRepository = logRepository;
        _settingsService = settingsService;
        _cfg = cfg.Value;

        _rpc = Solnet.Rpc.ClientFactory.GetClient(
            _cfg.Cluster == "MainNet" ? Solnet.Rpc.Cluster.MainNet : Solnet.Rpc.Cluster.DevNet);
    }

    public async Task<double?> GetSolPriceEurAsync(CancellationToken ct)
    {
        return await _priceService.GetSolPriceEurAsync(ct);
    }

    /// <summary>
    /// Überweist SOL von Treasury zur Zieladresse und reduziert die User Balance (zuerst Crypto, dann Physical).
    /// Returns: sweepSignature (tx sig)
    /// </summary>
    public async Task<(string Signature, Withdrawal WithdrawalRecord)> WithdrawAsync(
        int userId, 
        string withdrawalAddress, 
        double amountSol, 
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(withdrawalAddress))
            throw new ArgumentException("Withdrawal address is required.", nameof(withdrawalAddress));

        if (amountSol <= 0.0)
            throw new ArgumentException("Amount must be > 0.", nameof(amountSol));

        // 1) SOL Preis holen
        var solPriceEur = await _priceService.GetSolPriceEurAsync(ct);
        
        if (!solPriceEur.HasValue || solPriceEur.Value <= 0.0)
            throw new InvalidOperationException("SOL price unavailable. Try again.");

        double amountEur = amountSol * solPriceEur.Value;
        
        // 2) Settings abrufen und validieren
        var limits = await _settingsService.GetLimitsAsync();
        var cryptoFee = await _settingsService.GetCryptoFeeAsync();
        
        // Prüfe minimale Auszahlung
        if (amountEur < limits.MinWithdrawalEur)
            throw new InvalidOperationException($"Minimum withdrawal amount is {limits.MinWithdrawalEur:F2} EUR.");

        // 3) User abrufen
        var user = await _userRepository.GetByIdAsync(u => u.Id == userId, ct);
        if (user == null)
            throw new InvalidOperationException("User not found.");

        // Prüfe maximale tägliche Auszahlung (bereits erfolgte Auszahlungen heute)
        var today = DateTime.UtcNow.Date;
        var todaysWithdrawals = await _withdrawalRepository.ExecuteQueryAsync(
            query => query.Where(w => w.UserId == userId && 
                                     w.Timestamp.Date == today && 
                                     (w.Status == WithdrawalStatus.Approved || w.Status == WithdrawalStatus.Sent)),
            ct);
        
        double todaysTotal = todaysWithdrawals.Sum(w => w.EurAmount);
        
        if (todaysTotal + amountEur > limits.MaxDailyWithdrawalEur)
            throw new InvalidOperationException(
                $"Daily withdrawal limit exceeded. Already withdrawn today: {todaysTotal:F2} EUR, " +
                $"Limit: {limits.MaxDailyWithdrawalEur:F2} EUR.");

        // Prüfe maximale monatliche Auszahlung
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthlyWithdrawals = await _withdrawalRepository.ExecuteQueryAsync(
            query => query.Where(w => w.UserId == userId && 
                                     w.Timestamp >= monthStart &&
                                     (w.Status == WithdrawalStatus.Approved || w.Status == WithdrawalStatus.Sent)),
            ct);
        
        double monthlyTotal = monthlyWithdrawals.Sum(w => w.EurAmount);
        
        if (monthlyTotal + amountEur > limits.MaxMonthlyWithdrawalEur)
            throw new InvalidOperationException(
                $"Monthly withdrawal limit exceeded. Already withdrawn this month: {monthlyTotal:F2} EUR, " +
                $"Limit: {limits.MaxMonthlyWithdrawalEur:F2} EUR.");

        // 4) Berechne Gebühren
        double withdrawalFeePercent = cryptoFee.WithdrawalFeePercent;
        double withdrawalFeeEur = Math.Round(amountEur * (withdrawalFeePercent / 100), 2);
        
        // On-chain Netzwerkgebühr (ca. 0.000005 SOL)
        const ulong networkFeeLamports = 5_000UL;
        double networkFeeSol = networkFeeLamports / 1_000_000_000d;
        double networkFeeEur = networkFeeSol * solPriceEur.Value;
        
        double totalFeesEur = withdrawalFeeEur + networkFeeEur;
        double totalDebitEur = amountEur + totalFeesEur;

        // 5) User Balance check
        double totalBalance = user.BalancePhysical + user.BalanceCrypto;
        if (totalBalance < totalDebitEur)
            throw new InvalidOperationException(
                $"Insufficient total balance. Needed {totalDebitEur:0.00} EUR. " +
                $"Available: {totalBalance:0.00} EUR (Crypto: {user.BalanceCrypto:0.00}, Physical: {user.BalancePhysical:0.00}).");

        // 6) Treasury Balance check
        var treasuryWallet = new Wallet(_cfg.TreasuryMnemonic);
        var treasuryAccount = treasuryWallet.GetAccount(0);

        if (!string.Equals(treasuryAccount.PublicKey, _cfg.TreasuryAddress, StringComparison.Ordinal))
            throw new InvalidOperationException("TreasuryMnemonic does not match TreasuryAddress.");

        var treasuryBal = await _rpc.GetBalanceAsync(treasuryAccount.PublicKey, Commitment.Finalized);
        if (!treasuryBal.WasSuccessful)
            throw new InvalidOperationException("Failed to read treasury balance.");

        ulong needLamports = (ulong)Math.Ceiling(amountSol * 1_000_000_000d);
        ulong totalNeededLamports = needLamports + networkFeeLamports + 10_000UL; // + Buffer

        if (treasuryBal.Result.Value < totalNeededLamports)
        {
            throw new InvalidOperationException(
                $"Treasury has insufficient SOL to execute withdrawal. " +
                $"Available: {treasuryBal.Result.Value / 1_000_000_000.0:F6} SOL, " +
                $"Needed: {totalNeededLamports / 1_000_000_000.0:F6} SOL");
        }

        // 7) Log erstellen (wird später gespeichert)
        var logRecord = new Log
        {
            UserId = userId,
            ActionType = LogActionType.UserAction,
            UserType = LogUserType.User,
            Description = $"Withdrawal request initiated: {amountSol:F6} SOL to {withdrawalAddress}. " +
                         $"Fees: {totalFeesEur:F2} EUR, Total debit: {totalDebitEur:F2} EUR.",
            Date = DateTime.UtcNow
        };

        // 8) TX erstellen und senden
        var bh = await _rpc.GetLatestBlockHashAsync(Commitment.Finalized);
        if (!bh.WasSuccessful)
            throw new InvalidOperationException("Failed to get latest blockhash.");

        var toPubKey = new Solnet.Wallet.PublicKey(withdrawalAddress);

        var tx = new TransactionBuilder()
            .SetRecentBlockHash(bh.Result.Value.Blockhash)
            .SetFeePayer(treasuryAccount)
            .AddInstruction(SystemProgram.Transfer(
                fromPublicKey: treasuryAccount.PublicKey,
                toPublicKey: toPubKey,
                lamports: needLamports))
            .Build(new List<Account> { treasuryAccount });

        var send = await _rpc.SendTransactionAsync(tx, skipPreflight: false, Commitment.Finalized);
        if (!send.WasSuccessful || string.IsNullOrWhiteSpace(send.Result))
            throw new InvalidOperationException($"Withdraw transaction failed to send: {send.Reason}");

        string signature = send.Result;

        // 9) Finalize warten
        bool finalized = await WaitForFinalizedAsync(_rpc, signature, ct);
        if (!finalized)
            throw new InvalidOperationException("Withdraw tx not finalized (timeout / error).");

        // 10) Transaction erfolgreich - Alles speichern
        try
        {
            // 11) User Balance abziehen (zuerst von Crypto, dann von Physical)
            double remainingToDebit = totalDebitEur;
            double cryptoDebited = 0;
            double physicalDebited = 0;
            
            if (user.BalanceCrypto > 0)
            {
                cryptoDebited = Math.Min(user.BalanceCrypto, remainingToDebit);
                user.BalanceCrypto -= cryptoDebited;
                remainingToDebit -= cryptoDebited;
            }
            
            if (remainingToDebit > 0 && user.BalancePhysical >= remainingToDebit)
            {
                physicalDebited = remainingToDebit;
                user.BalancePhysical -= physicalDebited;
                remainingToDebit = 0;
            }

            if (remainingToDebit > 0)
                throw new InvalidOperationException("Insufficient balance after crypto debit.");

            // 12) Withdrawal Record erstellen
            var withdrawalRecord = new Withdrawal
            {
                UserId = userId,
                EurAmount = amountEur,
                Amount = amountSol,
                Asset = "SOL",
                TargetAddress = withdrawalAddress,
                Status = WithdrawalStatus.Sent,
                TxHash = signature,
                Details = $"Withdrawal completed - Amount: {amountSol:F6} SOL ({amountEur:F2} EUR), " +
                         $"Fees: {withdrawalFeeEur:F2} EUR ({withdrawalFeePercent}%) + " +
                         $"{networkFeeEur:F4} EUR (network), Total: {totalDebitEur:F2} EUR. " +
                         $"Transaction: {signature}",
                Timestamp = DateTime.UtcNow
            };

            // 13) Alle Repository-Operationen durchführen
            // User aktualisieren
            await _userRepository.UpdateAsync(user, ct);
            
            // Withdrawal speichern
            await _withdrawalRepository.AddAsync(withdrawalRecord, ct);
            
            // Log aktualisieren und speichern
            logRecord.Description += $" Transaction successful: {signature}. " +
                                     $"Balance updated: Crypto -{cryptoDebited:F2} EUR, Physical -{physicalDebited:F2} EUR.";
            await _logRepository.AddAsync(logRecord, ct);
            
            Console.WriteLine($"=== WITHDRAWAL COMPLETED SUCCESSFULLY ===");
            Console.WriteLine($"User: {userId}, Amount: {amountSol:F6} SOL ({amountEur:F2} EUR)");
            Console.WriteLine($"Fees: {totalFeesEur:F2} EUR (Platform: {withdrawalFeeEur:F2} + Network: {networkFeeEur:F4})");
            Console.WriteLine($"Total Debit: {totalDebitEur:F2} EUR");
            Console.WriteLine($"Balance Changes: Crypto -{cryptoDebited:F2}, Physical -{physicalDebited:F2}");
            Console.WriteLine($"New Balance: Crypto: {user.BalanceCrypto:F2}, Physical: {user.BalancePhysical:F2}");
            Console.WriteLine($"Transaction: {signature}");
            Console.WriteLine($"=== END ===");

            return (signature, withdrawalRecord);
        }
        catch (Exception ex)
        {
            // Falls etwas schiefgeht, zusätzlichen Error-Log erstellen
            var errorLog = new Log
            {
                UserId = userId,
                ActionType = LogActionType.Error,
                UserType = LogUserType.System,
                Description = $"Withdrawal failed after transaction success: {ex.Message}. " +
                             $"Transaction: {signature}. User balance not updated.",
                Date = DateTime.UtcNow
            };
            
            await _logRepository.AddAsync(errorLog, ct);
            throw new InvalidOperationException($"Database error during withdrawal: {ex.Message}", ex);
        }
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
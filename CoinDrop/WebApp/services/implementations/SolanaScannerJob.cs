using CoinDrop.config;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Solnet.Programs;
using Solnet.Rpc.Builders;
using Solnet.Wallet;
using System.Text.Json;
using WebApp.services.implementations;

namespace CoinDrop.services;

public sealed class SolBalanceScannerJob
{
    private readonly IHeliusSolanaService _heliusService;
    private readonly CryptoConfig _cfg;
    private readonly IPriceService _priceService;
    private readonly IRepository<CryptoDeposit> _cDepositRepo;
    private readonly IRepository<Log> _logRepo;
    private readonly IRepository<ApplicationUser> _userRepo;
    private readonly ILogger<SolBalanceScannerJob> _logger;
    private readonly HttpClient _httpClient; // HttpClient für lokale Requests
    
    private const ulong FeeReserveLamports = 10_000UL;
    private const ulong MinTriggerLamports = 10_000_000UL;
    private const ulong RentExemption = 890880UL;

    public SolBalanceScannerJob(
        IOptions<CryptoConfig> cfg,
        IHeliusSolanaService heliusService,
        IPriceService priceService,
        IRepository<CryptoDeposit> cDepositRepo,
        IRepository<Log> logRepo,
        IRepository<ApplicationUser> userRepo,
        ILogger<SolBalanceScannerJob> logger,
        IHttpClientFactory httpClientFactory) // HttpClientFactory injizieren
    {
        _cfg = cfg.Value;
        _heliusService = heliusService;
        _priceService = priceService;
        _cDepositRepo = cDepositRepo;
        _logRepo = logRepo;
        _userRepo = userRepo;
        _logger = logger;
        
        // HttpClient für lokale Requests erstellen
        _httpClient = httpClientFactory.CreateClient("Helius");
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var users = await _userRepo.Query()
            .Where(u => u.DepositAddress != null && u.DepositAddress != "")
            .Select(u => new { u.Id, DepositAddress = u.DepositAddress! })
            .ToListAsync(ct);

        if (users.Count == 0)
        {
            _logger.LogDebug("No users with deposit addresses found");
            return;
        }

        _logger.LogInformation("Scanning {Count} users for SOL deposits", users.Count);
        
        var addresses = users.Select(u => u.DepositAddress).ToList();
        var balances = await _heliusService.GetMultipleBalancesAsync(addresses, ct);
        
        var tasks = users.Select(user => ProcessUserIfBalanceAsync(
            user.Id, 
            user.DepositAddress, 
            balances.GetValueOrDefault(user.DepositAddress, 0),
            ct));

        await Task.WhenAll(tasks);
    }

    private async Task ProcessUserIfBalanceAsync(
        int userId, 
        string depositAddress, 
        decimal balanceSol,
        CancellationToken ct)
    {
        var balanceLamports = (ulong)(balanceSol * 1_000_000_000m);
        
        if (balanceLamports < MinTriggerLamports)
            return;

        _logger.LogInformation("Balance detected for user {UserId}: {Balance} SOL", 
            userId, balanceSol);

        try
        {
            await HandleUserAsync(userId, depositAddress, balanceLamports, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing user {UserId}", userId);
        }
    }

    private async Task HandleUserAsync(
        int userId, 
        string depositAddress, 
        ulong currentBalanceLamports,
        CancellationToken ct)
    {
        // 1. Find latest inbound transfer
        var inbound = await _heliusService.GetLatestInboundTransferAsync(depositAddress, ct);
        if (inbound == null)
        {
            _logger.LogWarning("No inbound transfer found for {Address}", depositAddress);
            return;
        }

        // 2. Verify deposit account
        Account depositAccount = GetDepositAccountFromUserId(userId);
        if (!string.Equals(depositAccount.PublicKey, depositAddress, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Deposit address mismatch for user {UserId}. Expected: {Expected}, Got: {Actual}", 
                userId, depositAddress, depositAccount.PublicKey);
            return;
        }

        // 3. Check for duplicate
        bool exists = await _cDepositRepo.Query()
            .AnyAsync(d => d.TxHash == inbound.Signature, ct);

        if (exists)
        {
            _logger.LogDebug("Deposit already processed: {Signature}", inbound.Signature);
            return;
        }

        // 4. Sweep funds
        var sweepResult = await SweepSolAsync(
            depositAccount,
            currentBalanceLamports,
            inbound.Signature,
            inbound.Amount,
            ct);

        if (sweepResult == null)
            return;

        // 5. Calculate amounts
        ulong grossLamports = inbound.Amount;
        ulong netLamports = grossLamports > FeeReserveLamports 
            ? grossLamports - FeeReserveLamports 
            : grossLamports;
        
        decimal netSol = netLamports / 1_000_000_000m;

        // 6. Get price
        double? solPriceEur = await _priceService.GetSolPriceEurAsync(ct) 
            ?? _priceService.GetCachedSolPriceEur();
        
        double eurAmount = solPriceEur.HasValue && solPriceEur.Value > 0.0
            ? (double)netSol * solPriceEur.Value
            : 0.0;

        // 7. Create deposit record
        var deposit = new CryptoDeposit
        {
            UserId = userId,
            EurAmount = eurAmount,
            Timestamp = DateTime.UtcNow,
            Details = $"Inbound: {inbound.Signature}; Sweep: {sweepResult.Value.Signature}; " +
                     $"Net: {netSol} SOL; EUR: {eurAmount:F2}",
            Network = "Solana",
            DepositAddress = depositAddress,
            SourceAddress = inbound.SourceAddress,
            Asset = "SOL",
            Amount = (double)netSol,
            TxHash = inbound.Signature
        };

        await _cDepositRepo.AddAsync(deposit, ct);

        // 8. Update user balance
        if (eurAmount > 0.0)
        {
            var user = await _userRepo.Query()
                .FirstOrDefaultAsync(u => u.Id == userId, ct);

            if (user != null)
            {
                user.BalanceCrypto += eurAmount;
                await _userRepo.UpdateAsync(user, ct);
            }
        }

        // 9. Log success
        var log = new Log
        {
            ActionType = LogActionType.UserAction,
            UserType = LogUserType.User,
            UserId = userId,
            Description = $"SOL deposit swept. {netSol} SOL ({eurAmount:F2} EUR) from {inbound.SourceAddress}",
            Date = DateTime.UtcNow
        };

        await _logRepo.AddAsync(log, ct);

        _logger.LogInformation(
            "✅ Deposit processed: User {UserId}, {NetSol} SOL, {EurAmount} EUR, Sweep: {SweepSig}",
            userId, netSol.ToString("F4"), eurAmount.ToString("F2"), sweepResult.Value.Signature);
    }

    private Account GetDepositAccountFromUserId(int userId)
    {
        var wallet = new Wallet(_cfg.MasterMnemonic);
        return wallet.GetAccount(userId);
    }

    private async Task<(string Signature, ulong Amount)?> SweepSolAsync(
        Account depositAccount,
        ulong currentBalanceLamports,
        string inboundSignature,
        ulong inboundAmount,
        CancellationToken ct)
    {
        try
        {
            string depositAddress = depositAccount.PublicKey;
            _logger.LogInformation("[SWEEP] Starting sweep from: {Address}", depositAddress);
            
            // 1. Prüfe ob Account existiert
            bool accountExists = await CheckAccountReallyExists(depositAddress, ct);
            
            if (!accountExists)
            {
                _logger.LogWarning("[SWEEP] Account not initialized on chain!");
                
                // Versuche Konto zu erstellen
                var createResult = await CreateSystemAccountIfNeeded(depositAccount, ct);
                if (!createResult)
                {
                    _logger.LogError("[SWEEP] Failed to initialize account");
                    return null;
                }
                
                _logger.LogInformation("[SWEEP] Account initialized, waiting for confirmation...");
                await Task.Delay(3000, ct);
            }
            
            // 2. Aktuelle Balance prüfen
            var actualBalance = await _heliusService.GetBalanceAsync(depositAddress, ct);
            ulong actualLamports = (ulong)(actualBalance * 1_000_000_000m);
            
            _logger.LogInformation("[SWEEP] Actual balance: {Balance} SOL ({Lamports} lamports)", 
                actualBalance, actualLamports);
            
            if (actualLamports < 10000) // Mindestens 0.00001 SOL
            {
                _logger.LogWarning("[SWEEP] Balance too low to sweep");
                return null;
            }
            
            // 3. Treasury holen
            var treasuryWallet = new Wallet(_cfg.TreasuryMnemonic);
            Account treasuryAccount = treasuryWallet.GetAccount(0);
            
            // Verifiziere Treasury
            if (!string.Equals(treasuryAccount.PublicKey, _cfg.TreasuryAddress, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("[SWEEP] Treasury mnemonic doesn't match config address");
                return null;
            }
            
            // 4. Sweep Amount berechnen (behalte etwas für zukünftige Fees)
            // Mindestens 5000 lamports für Rent + kleine Reserve
            ulong minKeep = RentExemption + 5000UL;
            ulong sweepLamports = actualLamports > minKeep 
                ? actualLamports - minKeep 
                : 0;
                
            if (sweepLamports == 0)
            {
                _logger.LogWarning("[SWEEP] Nothing to sweep after rent exemption");
                return null;
            }
            
            _logger.LogInformation("[SWEEP] Sweeping {Lamports} lamports ({Sol:F8} SOL)", 
                sweepLamports, (decimal)sweepLamports / 1_000_000_000m);
            
            // 5. Blockhash holen
            string blockhash = await _heliusService.GetLatestBlockhashAsync(ct);
            _logger.LogDebug("[SWEEP] Blockhash: {Blockhash}", blockhash);
            
            // 6. Transaction bauen
            var txBuilder = new TransactionBuilder()
                .SetRecentBlockHash(blockhash)
                .SetFeePayer(treasuryAccount)
                .AddInstruction(SystemProgram.Transfer(
                     new Solnet.Wallet.PublicKey(depositAddress),
                  new Solnet.Wallet.PublicKey(_cfg.TreasuryAddress),
                    lamports: sweepLamports
                ));
            
            // 7. Signieren und senden
            var tx = txBuilder.Build(new List<Account> { treasuryAccount, depositAccount });
            _logger.LogDebug("[SWEEP] Transaction built: {Size} bytes", tx.Length);
            
            _logger.LogInformation("[SWEEP] Sending sweep transaction...");
            var signature = await _heliusService.SendTransactionAsync(tx, ct);
            
            _logger.LogInformation("[SWEEP] ✅ Success! Signature: {Signature}", signature);
            return (signature, sweepLamports);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SWEEP] Failed to sweep from {Address}", depositAccount.PublicKey);
            return null;
        }
    }

    private async Task<bool> CheckAccountReallyExists(string address, CancellationToken ct)
    {
        try
        {
            var request = new
            {
                jsonrpc = "2.0",
                id = "check_acc",
                method = "getAccountInfo",
                @params = new object[] { address }
            };

            var response = await _httpClient.PostAsJsonAsync("", request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("value", out var value) &&
                value.ValueKind != JsonValueKind.Null)
            {
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ACCOUNT_CHECK] Failed to check account {Address}", address);
            return false;
        }
    }

    private async Task<bool> CreateSystemAccountIfNeeded(Account depositAccount, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("[ACCOUNT_CREATE] Creating system account: {Address}", depositAccount.PublicKey);
            
            var treasuryWallet = new Wallet(_cfg.TreasuryMnemonic);
            Account treasuryAccount = treasuryWallet.GetAccount(0);
            
            // Rent-Exemption
            ulong rentExemption = 890880UL;
            
            // Blockhash
            string blockhash = await _heliusService.GetLatestBlockhashAsync(ct);
            
            // Transaction: Create Account
            var txBuilder = new TransactionBuilder()
                .SetRecentBlockHash(blockhash)
                .SetFeePayer(treasuryAccount)
                .AddInstruction(SystemProgram.CreateAccount(
                    fromAccount: treasuryAccount,
                   depositAccount,
                    lamports: rentExemption,
                    space: 0,
                    programId: SystemProgram.ProgramIdKey
                ));
            
            var tx = txBuilder.Build(new List<Account> { treasuryAccount, depositAccount });
            
            _logger.LogInformation("[ACCOUNT_CREATE] Creating account...");
            var signature = await _heliusService.SendTransactionAsync(tx, ct);
            
            _logger.LogInformation("[ACCOUNT_CREATE] ✅ Account creation sent: {Signature}", signature);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ACCOUNT_CREATE] Failed to create account");
            return false;
        }
    }
}
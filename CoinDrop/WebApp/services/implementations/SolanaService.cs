using CoinDrop.config;
using CoinDrop.services.interfaces;
using Microsoft.Extensions.Options;
using Solnet.Wallet;
using Solnet.Wallet.Bip39;
using Solnet.Programs;
using Solnet.Rpc.Builders;
using System.Text.Json;
using SimpleBase;
using Microsoft.Extensions.Logging;

namespace CoinDrop.services;

public class SolanaWalletService : ISolanService
{
    private readonly Wallet _masterWallet;
    private readonly CryptoConfig _cfg;
    private readonly ILogger<SolanaWalletService> _logger;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, bool> _initializedCache = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SolanaWalletService(
        IOptions<CryptoConfig> cfg,
        ILogger<SolanaWalletService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _cfg = cfg.Value;
        _logger = logger;
        _masterWallet = new Wallet(
            new Mnemonic(_cfg.MasterMnemonic, WordList.English)
        );
        
        // HttpClient für Helius Requests
        _httpClient = httpClientFactory.CreateClient("Helius");
    }

    public string GetUserDepositAddress(int userIndex)
    {
        var account = _masterWallet.GetAccount(userIndex);
        string address = account.PublicKey;
        
        // ASYNCHRONE Initialisierung starten (fire-and-forget)
        // Nicht blockieren, aber sicherstellen dass Account existiert
        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureAccountInitializedAsync(address, userIndex, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-initialize account {Address}", address);
            }
        });
        
        return address;
    }

    // Private Methode für Initialisierung
    private async Task EnsureAccountInitializedAsync(string address, int userIndex, CancellationToken ct)
    {
        // Cache prüfen (verhindert doppelte Initialisierung)
        if (_initializedCache.ContainsKey(address))
            return;
        
        await _initLock.WaitAsync(ct);
        try
        {
            // Nochmal prüfen nach Lock
            if (_initializedCache.ContainsKey(address))
                return;
            
            // 1. Prüfe ob Account auf Blockchain existiert
            bool exists = await CheckAccountExistsOnChain(address, ct);
            
            if (exists)
            {
                _initializedCache[address] = true;
                _logger.LogDebug("Account {Address} already exists on chain", address);
                return;
            }
            
            // 2. Account erstellen
            _logger.LogInformation("🔧 Auto-creating account for user {UserIndex}: {Address}", 
                userIndex, address);
            
            bool created = await CreateAccountOnBlockchain(address, userIndex, ct);
            
            if (created)
            {
                _initializedCache[address] = true;
                _logger.LogInformation("✅ Auto-created account: {Address}", address);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<bool> CheckAccountExistsOnChain(string address, CancellationToken ct)
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
                result.TryGetProperty("value", out var value))
            {
                return value.ValueKind != JsonValueKind.Null;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to check account existence for {Address}", address);
            return false;
        }
    }

    private async Task<bool> CreateAccountOnBlockchain(string address, int userIndex, CancellationToken ct)
    {
        try
        {
            // 1. Account-Objekt für Signatur
            var account = _masterWallet.GetAccount(userIndex);
            
            // 2. Treasury Wallet
            var treasuryWallet = new Wallet(_cfg.TreasuryMnemonic);
            Account treasuryAccount = treasuryWallet.GetAccount(0);
            
            // 3. Blockhash holen
            var blockhashRequest = new
            {
                jsonrpc = "2.0",
                id = "blockhash",
                method = "getLatestBlockhash",
                @params = new object[] { new { commitment = "finalized" } }
            };
            
            var response = await _httpClient.PostAsJsonAsync("", blockhashRequest, ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            
            string blockhash = "";
            using (var doc = JsonDocument.Parse(content))
            {
                if (doc.RootElement.TryGetProperty("result", out var result) &&
                    result.TryGetProperty("value", out var value) &&
                    value.TryGetProperty("blockhash", out var bh))
                {
                    blockhash = bh.GetString() ?? "";
                }
            }
            
            if (string.IsNullOrEmpty(blockhash))
            {
                _logger.LogError("Failed to get blockhash");
                return false;
            }
            
            // 4. Create Account Transaction
            ulong initialLamports = 1_000_000UL; // 0.001 SOL (Rent + Buffer)
            
            var txBuilder = new TransactionBuilder()
                .SetRecentBlockHash(blockhash)
                .SetFeePayer(treasuryAccount)
                .AddInstruction(SystemProgram.CreateAccount(
                    fromAccount: treasuryAccount,
                     account,
                    lamports: initialLamports,
                    space: 0,
                    programId: SystemProgram.ProgramIdKey
                ));
            
            var tx = txBuilder.Build(new List<Account> { treasuryAccount, account });
            
            // 5. Base58 Encoding
            string base58Tx = Base58.Bitcoin.Encode(tx);
            
            // 6. Send Transaction
            var sendRequest = new
            {
                jsonrpc = "2.0",
                id = "create_acc",
                method = "sendTransaction",
                @params = new object[] { base58Tx }
            };
            
            var sendResponse = await _httpClient.PostAsJsonAsync("", sendRequest, ct);
            var sendContent = await sendResponse.Content.ReadAsStringAsync(ct);
            
            _logger.LogDebug("Create account response: {Response}", sendContent);
            
            if (!sendResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to create account: {StatusCode}", sendResponse.StatusCode);
                return false;
            }
            
            // 7. Prüfe auf Error in Response
            using var sendDoc = JsonDocument.Parse(sendContent);
            if (sendDoc.RootElement.TryGetProperty("error", out var error))
            {
                _logger.LogError("RPC error creating account: {Error}", error);
                return false;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create account on blockchain");
            return false;
        }
    }

    // Optional: Methode um Account-Objekt für Sweep zu holen
    public Account GetUserAccount(int userIndex)
    {
        return _masterWallet.GetAccount(userIndex);
    }
}
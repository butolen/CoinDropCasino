using System.Text.Json;
using SimpleBase;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CoinDrop.services;

public interface IHeliusSolanaService
{
    Task<decimal> GetBalanceAsync(string address, CancellationToken ct);
    Task<Dictionary<string, decimal>> GetMultipleBalancesAsync(List<string> addresses, CancellationToken ct);
    Task<TransactionInfo?> GetLatestInboundTransferAsync(string address, CancellationToken ct);
    Task<string> SendTransactionAsync(byte[] transaction, CancellationToken ct);
    Task<ulong> GetCurrentFeeAsync(CancellationToken ct);
    Task<string> GetLatestBlockhashAsync(CancellationToken ct);
}

public class HeliusSolanaService : IHeliusSolanaService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HeliusSolanaService> _logger;
    private readonly string _apiKey;
    private readonly SemaphoreSlim _rateLimiter;
    
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    public HeliusSolanaService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IConfiguration config,
        ILogger<HeliusSolanaService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Helius");
        _cache = cache;
        _logger = logger;
        _apiKey = config["Helius:ApiKey"] ?? throw new ArgumentNullException("Helius:ApiKey");
        _rateLimiter = new SemaphoreSlim(10, 10); // 10 requests pro Sekunde
    }

    // 1. EINFACHE BALANCE ABFRAGE - Laut Helius Docs
    public async Task<decimal> GetBalanceAsync(string address, CancellationToken ct)
    {
        var cacheKey = $"sol_balance_{address}";
        
        if (_cache.TryGetValue<decimal>(cacheKey, out var cachedBalance))
            return cachedBalance;

        await _rateLimiter.WaitAsync(ct);
        try
        {
            // GENAU WIE IN DEN DOCS: https://www.helius.dev/docs/rpc/guides/getbalance
            var request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "getBalance",
                @params = new[] { address }
            };

            var response = await _httpClient.PostAsJsonAsync("", request, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Helius getBalance failed for {Address}: {StatusCode} - {Error}", 
                    address, response.StatusCode, error);
                
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    throw new UnauthorizedAccessException($"Helius API-Key ungültig: {error}");
                }
                
                response.EnsureSuccessStatusCode();
            }

            var result = await response.Content.ReadFromJsonAsync<HeliusBalanceResponse>(_jsonOptions, ct);
            
            // Lamports zu SOL konvertieren
            var balance = (result?.Result?.Value ?? 0) / 1_000_000_000m;
            
            // Cache für 15 Sekunden
            _cache.Set(cacheKey, balance, TimeSpan.FromSeconds(15));
            
            return balance;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    // 2. MEHRERE BALANCES - Serial statt Batch
    public async Task<Dictionary<string, decimal>> GetMultipleBalancesAsync(List<string> addresses, CancellationToken ct)
    {
        var results = new Dictionary<string, decimal>();
        
        // Einfach sequentiell abfragen (10/sec = 600/min = 36.000/Stunde)
        foreach (var address in addresses)
        {
            if (ct.IsCancellationRequested) break;
            
            try
            {
                var balance = await GetBalanceAsync(address, ct);
                results[address] = balance;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get balance for {Address}", address);
                results[address] = 0;
            }
        }
        
        return results;
    }

    // 3. TRANSACTION HISTORY - Auch einfach
    public async Task<TransactionInfo?> GetLatestInboundTransferAsync(string address, CancellationToken ct)
    {
        await _rateLimiter.WaitAsync(ct);
        try
        {
            // Get signatures
            var sigRequest = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "getSignaturesForAddress",
                @params = new object[]
                {
                    address,
                    new { limit = 5, commitment = "finalized" }
                }
            };

            var sigResponse = await _httpClient.PostAsJsonAsync("", sigRequest, ct);
            sigResponse.EnsureSuccessStatusCode();
            
            var sigResult = await sigResponse.Content.ReadFromJsonAsync<HeliusSignaturesResponse>(_jsonOptions, ct);
            
            if (sigResult?.Result == null || sigResult.Result.Count == 0)
                return null;

            // Check latest signature
            var latestSig = sigResult.Result[0];
            
            // Get transaction details
            var txRequest = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "getTransaction",
                @params = new object[]
                {
                    latestSig.Signature,
                    new { encoding = "json", commitment = "finalized" }
                }
            };

            var txResponse = await _httpClient.PostAsJsonAsync("", txRequest, ct);
            txResponse.EnsureSuccessStatusCode();
            
            var txResult = await txResponse.Content.ReadFromJsonAsync<TransactionResponse>(_jsonOptions, ct);
            
            return txResult?.Result?.ToTransactionInfo(address);
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

  
public async Task<string> SendTransactionAsync(byte[] transaction, CancellationToken ct)
{
    await _rateLimiter.WaitAsync(ct);
    try
    {
        Console.WriteLine($"[HELIUS_TX] Transaction size: {transaction.Length} bytes");
        
        // 1. KORREKT: Base58 Encoding verwenden (nicht Base64!)
        string base58Transaction = Base58.Bitcoin.Encode(transaction);
        Console.WriteLine($"[HELIUS_TX] Base58 encoded: {base58Transaction.Length} chars");
        Console.WriteLine($"[HELIUS_TX] First 50 chars: {base58Transaction.Substring(0, Math.Min(50, base58Transaction.Length))}...");
        
        // 2. Prüfe ob Base58 gültig ist (keine Base64-Zeichen wie '/', '+', '=')
        if (base58Transaction.Contains('/') || base58Transaction.Contains('+') || base58Transaction.Contains('='))
        {
            Console.WriteLine($"[HELIUS_TX_WARN] Warning: Base58 string contains Base64 characters!");
        }

        // 3. Request gemäß Helius Docs
        var request = new
        {
            jsonrpc = "2.0",
            id = "1",
            method = "sendTransaction",
            @params = new object[] { base58Transaction } // Nur Base58-String, kein Objekt!
        };

        Console.WriteLine($"[HELIUS_TX] Sending to Helius...");
        
        var response = await _httpClient.PostAsJsonAsync("", request, ct);
        var responseContent = await response.Content.ReadAsStringAsync(ct);
        
        Console.WriteLine($"[HELIUS_TX] Response: {responseContent}");

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[HELIUS_TX_ERROR] HTTP {response.StatusCode}: {responseContent}");
            response.EnsureSuccessStatusCode();
        }

        // Parse response
        using var doc = JsonDocument.Parse(responseContent);
        var root = doc.RootElement;
        
        if (root.TryGetProperty("error", out var errorElement))
        {
            string errorMsg = errorElement.ToString();
            if (errorElement.TryGetProperty("message", out var msg))
                errorMsg = msg.GetString() ?? errorMsg;
            
            Console.WriteLine($"[HELIUS_TX_ERROR] {errorMsg}");
            throw new Exception($"Helius: {errorMsg}");
        }
        
        if (root.TryGetProperty("result", out var resultElement))
        {
            var signature = resultElement.GetString();
            Console.WriteLine($"[HELIUS_TX_SUCCESS] ✅ Signature: {signature}");
            return signature;
        }
        
        throw new Exception("No result in response");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[HELIUS_TX_FAILED] {ex.Message}");
        throw;
    }
    finally
    {
        _rateLimiter.Release();
    }
}

    // 5. CURRENT FEE - Einfach
    public async Task<ulong> GetCurrentFeeAsync(CancellationToken ct)
    {
        var cacheKey = "current_fee";
        if (_cache.TryGetValue<ulong>(cacheKey, out var fee))
            return fee;

        await _rateLimiter.WaitAsync(ct);
        try
        {
            // Einfache Fee-Abfrage
            var request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "getRecentPrioritizationFees",
                @params = new[] { new string[0] }
            };

            var response = await _httpClient.PostAsJsonAsync("", request, ct);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<FeeResponse>(_jsonOptions, ct);
            fee = result?.Result?.FirstOrDefault()?.PrioritizationFee ?? 5000;
            
            _cache.Set(cacheKey, fee, TimeSpan.FromMinutes(5));
            return fee;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    // 6. BLOCKHASH - Einfach
    public async Task<string> GetLatestBlockhashAsync(CancellationToken ct)
    {
        await _rateLimiter.WaitAsync(ct);
        try
        {
            var request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "getLatestBlockhash",
                @params = new[] { new { commitment = "finalized" } }
            };

            var response = await _httpClient.PostAsJsonAsync("", request, ct);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<BlockhashResponse>(_jsonOptions, ct);
            return result?.Result?.Value?.Blockhash ?? throw new Exception("No blockhash");
        }
        finally
        {
            _rateLimiter.Release();
        }
    }
}

// 7. EINFACHE RESPONSE KLASSEN
public class HeliusBalanceResponse
{
    [JsonPropertyName("result")]
    public BalanceResult Result { get; set; }
    
    public class BalanceResult
    {
        [JsonPropertyName("value")]
        public ulong Value { get; set; }
    }
}

public class HeliusSignaturesResponse
{
    [JsonPropertyName("result")]
    public List<SignatureInfo> Result { get; set; }
    
    public class SignatureInfo
    {
        [JsonPropertyName("signature")]
        public string Signature { get; set; }
    }
}

public class TransactionResponse
{
    [JsonPropertyName("result")]
    public TransactionResult Result { get; set; }
}

public class TransactionResult
{
    [JsonPropertyName("transaction")]
    public TransactionData Transaction { get; set; }
    
    [JsonPropertyName("meta")]
    public TransactionMeta Meta { get; set; }
}

public class TransactionData
{
    [JsonPropertyName("message")]
    public MessageData Message { get; set; }
    
    [JsonPropertyName("signatures")]
    public string[] Signatures { get; set; }
}

public class MessageData
{
    [JsonPropertyName("accountKeys")]
    public string[] AccountKeys { get; set; }
}

public class TransactionMeta
{
    [JsonPropertyName("preBalances")]
    public ulong[] PreBalances { get; set; }
    
    [JsonPropertyName("postBalances")]
    public ulong[] PostBalances { get; set; }
}

public class SendTransactionResponse
{
    [JsonPropertyName("result")]
    public string Result { get; set; }
}

public class FeeResponse
{
    [JsonPropertyName("result")]
    public List<FeeResult> Result { get; set; }
    
    public class FeeResult
    {
        [JsonPropertyName("prioritizationFee")]
        public ulong PrioritizationFee { get; set; }
    }
}

public class BlockhashResponse
{
    [JsonPropertyName("result")]
    public BlockhashResult Result { get; set; }
    
    public class BlockhashResult
    {
        [JsonPropertyName("value")]
        public BlockhashValue Value { get; set; }
    }
    
    public class BlockhashValue
    {
        [JsonPropertyName("blockhash")]
        public string Blockhash { get; set; }
    }
}

// 8. TRANSACTION INFO HELPER
public class TransactionInfo
{
    public string Signature { get; set; }
    public string SourceAddress { get; set; }
    public string DestinationAddress { get; set; }
    public ulong Amount { get; set; }
    
    public bool IsInboundTo(string address) => 
        DestinationAddress == address && Amount > 0;
}

public static class TransactionExtensions
{
    public static TransactionInfo ToTransactionInfo(this TransactionResult result, string targetAddress)
    {
        if (result?.Transaction?.Message?.AccountKeys == null ||
            result.Meta?.PreBalances == null ||
            result.Meta?.PostBalances == null)
            return null;

        var info = new TransactionInfo();
        
        // Signature
        if (result.Transaction.Signatures != null && result.Transaction.Signatures.Length > 0)
        {
            info.Signature = result.Transaction.Signatures[0];
        }

        // Find balance changes
        var accounts = result.Transaction.Message.AccountKeys;
        var pre = result.Meta.PreBalances;
        var post = result.Meta.PostBalances;
        
        int targetIndex = -1;
        for (int i = 0; i < accounts.Length; i++)
        {
            if (accounts[i] == targetAddress)
            {
                targetIndex = i;
                break;
            }
        }
        
        if (targetIndex == -1 || targetIndex >= pre.Length || targetIndex >= post.Length)
            return null;
        
        // Check if target received funds
        long targetChange = (long)post[targetIndex] - (long)pre[targetIndex];
        if (targetChange <= 0)
            return null;
        
        info.DestinationAddress = targetAddress;
        info.Amount = (ulong)targetChange;
        
        // Find sender (biggest negative change)
        string sender = null;
        long maxNegative = 0;
        
        for (int i = 0; i < accounts.Length && i < pre.Length && i < post.Length; i++)
        {
            long change = (long)post[i] - (long)pre[i];
            if (change < maxNegative)
            {
                maxNegative = change;
                sender = accounts[i];
            }
        }
        
        info.SourceAddress = sender;
        return info;
    }
}
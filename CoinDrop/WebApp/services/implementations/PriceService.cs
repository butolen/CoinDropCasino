using Microsoft.Extensions.Caching.Memory;

namespace WebApp.services.implementations;

public interface IPriceService
{
    Task<double?> GetSolPriceEurAsync(CancellationToken ct);
    double? GetCachedSolPriceEur();
}

public sealed class PriceService : IPriceService
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private const string CacheKey = "SolPriceEur";
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(30); // 30 Sekunden Cache

    public PriceService(HttpClient http, IMemoryCache cache)
    {
        _http = http;
        _cache = cache;
    }

    public async Task<double?> GetSolPriceEurAsync(CancellationToken ct)
    {
        // Versuche zuerst aus dem Cache
        if (_cache.TryGetValue(CacheKey, out double? cachedPrice))
        {
            Console.WriteLine($"SOL price from cache: {cachedPrice} EUR");
            return cachedPrice;
        }

        const string url = "https://api.coingecko.com/api/v3/simple/price?ids=solana&vs_currencies=eur";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        
            using var res = await _http.SendAsync(request, ct);
        
            if (!res.IsSuccessStatusCode)
            {
                Console.WriteLine($"API request failed: {res.StatusCode}");
                return null;
            }

            var json = await res.Content.ReadFromJsonAsync<Dictionary<string, Dictionary<string, double>>>(cancellationToken: ct);
            if (json == null)
            {
                Console.WriteLine("JSON response is null");
                return null;
            }

            if (!json.TryGetValue("solana", out var inner))
            {
                Console.WriteLine("Key 'solana' not found");
                return null;
            }

            if (!inner.TryGetValue("eur", out var price))
            {
                Console.WriteLine("Key 'eur' not found");
                return null;
            }
        
            Console.WriteLine($"SOL price retrieved from API: {price} EUR");
            
            // In Cache speichern
            _cache.Set(CacheKey, price, _cacheDuration);
            
            return price;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting SOL price: {ex.Message}");
            return null;
        }
    }

    public double? GetCachedSolPriceEur()
    {
        _cache.TryGetValue(CacheKey, out double? price);
        return price;
    }
}
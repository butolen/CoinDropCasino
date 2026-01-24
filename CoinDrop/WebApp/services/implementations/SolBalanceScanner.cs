using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoinDrop.services;

public sealed class SolBalanceScannerHosted : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SolBalanceScannerHosted> _logger;
    private readonly SemaphoreSlim _scannerLock = new(1, 1);

    public SolBalanceScannerHosted(
        IServiceScopeFactory scopeFactory,
        ILogger<SolBalanceScannerHosted> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("🚀 SolBalanceScanner (Helius) started");
        
        // Initial delay
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Ensure only one scanner runs at a time
                await _scannerLock.WaitAsync(ct);
                
                using var scope = _scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<SolBalanceScannerJob>();
                
                _logger.LogDebug("Starting scan cycle");
                await job.RunOnceAsync(ct);
                _logger.LogDebug("Scan cycle completed");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("SolBalanceScanner shutting down");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Critical error in scanner");
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            finally
            {
                if (_scannerLock.CurrentCount == 0)
                    _scannerLock.Release();
            }
            
            // Wait before next scan cycle
            await Task.Delay(TimeSpan.FromSeconds(15), ct);
        }
    }
}
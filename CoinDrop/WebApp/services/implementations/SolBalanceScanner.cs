using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoinDrop.services;

public sealed class SolBalanceScannerHosted : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly TimeSpan LoopDelay = TimeSpan.FromSeconds(10);

    public SolBalanceScannerHosted(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<SolBalanceScannerJob>();
                await job.RunOnceAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // normal shutdown
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SOL_SCAN_FATAL] {ex}");
            }

            await Task.Delay(LoopDelay, ct);
        }
    }
}
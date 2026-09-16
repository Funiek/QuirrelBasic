using QuirrelBasic.Models;
using QuirrelBasic.Services;
namespace QuirrelBasic;

public sealed class Worker(SyncEngine engine, DrivesConfig config, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await engine.RunAsync(false, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Synchronization failed. Retrying after the configured interval."); }
            await Task.Delay(TimeSpan.FromSeconds(config.IntervalSeconds), stoppingToken);
        }
    }
}

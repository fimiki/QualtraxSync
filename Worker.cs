using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QualtraxSync.Services.Services;

namespace QualtraxSync;

internal sealed class Worker(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<Services.Options> options,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();

                var sync = scope.ServiceProvider.GetRequiredService<ISyncService>();

                await sync.ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The synchronization run failed and will be retried on the next interval.");
            }

            var interval = TimeSpan.FromSeconds(Math.Max(1, options.CurrentValue.IntervalSeconds));

            logger.LogDebug("Next synchronization run in {Interval}.", interval);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

using Gamex.GameCatalog.Application.Refresh;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gamex.GameCatalog.Infrastructure.Jobs;

/// <summary>Runs <see cref="INightlyRefreshService"/> daily at the configured local time on one instance only.</summary>
internal sealed partial class NightlyRefreshScheduler(
    NightlySchedule schedule,
    MySqlJobLock jobLock,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<NightlyRefreshScheduler> logger) : BackgroundService
{
    public const string LockName = "gamex-catalog-nightly-refresh";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = time.GetUtcNow().UtcDateTime;
            var next = schedule.NextRunUtc(now);
            LogScheduled(logger, next);

            try
            {
                await Task.Delay(next - now, time, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed run must not stop the schedule; tomorrow's run retries.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogFailed(logger, ex);
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        await using var handle = await jobLock.TryAcquireAsync(LockName, ct);
        if (handle is null)
        {
            LogSkipped(logger);
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INightlyRefreshService>().RunAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Next nightly refresh at {NextRunUtc:u}")]
    private static partial void LogScheduled(ILogger logger, DateTime nextRunUtc);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nightly refresh skipped: another instance holds the lock")]
    private static partial void LogSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Nightly refresh failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

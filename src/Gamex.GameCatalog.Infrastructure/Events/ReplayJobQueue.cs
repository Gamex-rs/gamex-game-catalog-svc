using System.Threading.Channels;
using Gamex.GameCatalog.Application.Abstractions;
using Gamex.GameCatalog.Application.Games;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gamex.GameCatalog.Infrastructure.Events;

internal sealed class ReplayJobQueue : IReplayJobQueue
{
    private readonly Channel<ReplayRequest> _channel = Channel.CreateBounded<ReplayRequest>(
        new BoundedChannelOptions(10) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });

    public ChannelReader<ReplayRequest> Reader => _channel.Reader;

    public bool TryEnqueue(ReplayRequest request) => _channel.Writer.TryWrite(request);
}

/// <summary>Runs queued replays one at a time.</summary>
internal sealed partial class ReplayWorker(
    ReplayJobQueue queue,
    IServiceScopeFactory scopes,
    ILogger<ReplayWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            LogStarted(logger, request.JobId);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var replay = scope.ServiceProvider.GetRequiredService<ICatalogReplayService>();
                await replay.ReplayAsync(request.GameIds, stoppingToken);
                LogFinished(logger, request.JobId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed replay must not stop the worker; the admin can trigger it again.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogFailed(logger, ex, request.JobId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog event replay {JobId} started")]
    private static partial void LogStarted(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog event replay {JobId} finished")]
    private static partial void LogFinished(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Catalog event replay {JobId} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid jobId);
}

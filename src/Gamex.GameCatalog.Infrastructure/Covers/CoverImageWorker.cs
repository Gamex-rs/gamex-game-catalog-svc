using Gamex.GameCatalog.Application.Games;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gamex.GameCatalog.Infrastructure.Covers;

/// <summary>Copies queued covers to S3 one at a time and attaches the CDN URL to the game.</summary>
internal sealed partial class CoverImageWorker(
    CoverImageQueue queue,
    S3CoverImageStore store,
    IServiceScopeFactory scopes,
    ILogger<CoverImageWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var url = await store.CopyAsync(request, stoppingToken);

                await using var scope = scopes.CreateAsyncScope();
                var attacher = scope.ServiceProvider.GetRequiredService<ICoverImageAttacher>();
                await attacher.AttachAsync(request.GameId, url, request.SourceUrl, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // One bad cover must not stop the worker; the next refresh retries it.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogCoverFailed(logger, ex, request.GameId, request.SourceUrl);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Copying cover for game {GameId} from {SourceUrl} failed")]
    private static partial void LogCoverFailed(ILogger logger, Exception exception, Guid gameId, string sourceUrl);
}

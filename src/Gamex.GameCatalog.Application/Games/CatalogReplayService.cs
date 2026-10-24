using Gamex.GameCatalog.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Gamex.GameCatalog.Application.Games;

public sealed record ReplayRequest(Guid JobId, IReadOnlyList<Guid>? GameIds);

public interface ICatalogReplayService
{
    /// <summary>Republishes platforms, then games, so consumers can rebuild their replicas.</summary>
    /// <param name="gameIds">Only these games (and their platforms); all when <c>null</c>.</param>
    Task ReplayAsync(IReadOnlyList<Guid>? gameIds, CancellationToken cancellationToken);
}

internal sealed partial class CatalogReplayService(
    ICatalogReadRepository repository,
    ICatalogEventPublisher events,
    ILogger<CatalogReplayService> logger) : ICatalogReplayService
{
    public const int BatchSize = 100;

    public async Task ReplayAsync(IReadOnlyList<Guid>? gameIds, CancellationToken cancellationToken)
    {
        var platforms = gameIds is null
            ? await repository.GetAllPlatformsAsync(cancellationToken)
            : await repository.GetPlatformsOfGamesAsync(gameIds, cancellationToken);

        if (platforms.Count > 0)
        {
            await events.PublishPlatformsAsync(platforms, cancellationToken);
        }

        var gameCount = 0;
        if (gameIds is null)
        {
            while (true)
            {
                var batch = await repository.GetGamesBatchAsync(gameCount, BatchSize, cancellationToken);
                if (batch.Count == 0)
                {
                    break;
                }

                await events.PublishGamesAsync(batch, cancellationToken);
                gameCount += batch.Count;
            }
        }
        else
        {
            foreach (var chunk in gameIds.Distinct().Chunk(BatchSize))
            {
                var batch = await repository.GetGamesByIdAsync(chunk, cancellationToken);
                if (batch.Count > 0)
                {
                    await events.PublishGamesAsync(batch, cancellationToken);
                    gameCount += batch.Count;
                }
            }
        }

        LogReplayed(logger, platforms.Count, gameCount);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Replayed {Platforms} platforms and {Games} games")]
    private static partial void LogReplayed(ILogger logger, int platforms, int games);
}

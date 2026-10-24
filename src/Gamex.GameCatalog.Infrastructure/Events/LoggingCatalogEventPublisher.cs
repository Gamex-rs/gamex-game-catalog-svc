using Gamex.GameCatalog.Application.Abstractions;
using Gamex.GameCatalog.Domain.Games;
using Microsoft.Extensions.Logging;

namespace Gamex.GameCatalog.Infrastructure.Events;

/// <summary>Placeholder until the SQS publisher exists: logs instead of publishing.</summary>
internal sealed partial class LoggingCatalogEventPublisher(ILogger<LoggingCatalogEventPublisher> logger) : ICatalogEventPublisher
{
    public Task PublishPlatformsAsync(IReadOnlyCollection<Platform> platforms, CancellationToken cancellationToken)
    {
        foreach (var platform in platforms)
        {
            LogEvent(logger, "catalog.platform.upserted", platform.Id);
        }

        return Task.CompletedTask;
    }

    public Task PublishGamesAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken)
    {
        foreach (var game in games)
        {
            LogEvent(logger, "catalog.game.upserted", game.Id);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Would publish {EventType} for {Id}")]
    private static partial void LogEvent(ILogger logger, string eventType, Guid id);
}

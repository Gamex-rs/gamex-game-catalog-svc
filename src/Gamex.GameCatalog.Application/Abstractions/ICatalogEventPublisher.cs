using Gamex.GameCatalog.Domain.Games;

namespace Gamex.GameCatalog.Application.Abstractions;

/// <summary>Publishes <c>catalog.*</c> events to <c>gamex-catalog-events</c>. Called after the change is saved.</summary>
public interface ICatalogEventPublisher
{
    Task PublishPlatformsAsync(IReadOnlyCollection<Platform> platforms, CancellationToken cancellationToken);

    /// <remarks>Games must have their platforms loaded.</remarks>
    Task PublishGamesAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken);
}

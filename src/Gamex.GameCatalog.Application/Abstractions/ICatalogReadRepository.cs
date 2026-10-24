using Gamex.GameCatalog.Domain.Games;

namespace Gamex.GameCatalog.Application.Abstractions;

/// <summary>Untracked bulk reads of catalog aggregates, e.g. for event replay.</summary>
public interface ICatalogReadRepository
{
    Task<List<Platform>> GetAllPlatformsAsync(CancellationToken cancellationToken);

    Task<List<Platform>> GetPlatformsOfGamesAsync(IReadOnlyCollection<Guid> gameIds, CancellationToken cancellationToken);

    /// <summary>Games with platforms, ordered by id. Offset paging: replay is idempotent, so a shifted page only repeats or skips games that publish their own events anyway.</summary>
    Task<List<Game>> GetGamesBatchAsync(int skip, int take, CancellationToken cancellationToken);

    /// <summary>Games with platforms.</summary>
    Task<List<Game>> GetGamesByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

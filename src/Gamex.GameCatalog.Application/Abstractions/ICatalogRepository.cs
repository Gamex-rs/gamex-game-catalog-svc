using Gamex.GameCatalog.Domain.Games;

namespace Gamex.GameCatalog.Application.Abstractions;

/// <summary>Tracked access to catalog aggregates for writes.</summary>
public interface ICatalogRepository
{
    Task<List<Platform>> GetPlatformsBySlugAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);

    Task<List<Genre>> GetGenresBySlugAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);

    Task<List<Platform>> GetPlatformsByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<List<Genre>> GetGenresByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Loads games with platforms and genres.</summary>
    Task<List<Game>> GetGamesByRawgIdAsync(IReadOnlyCollection<int> rawgIds, CancellationToken cancellationToken);

    /// <summary>Loads a game with platforms and genres.</summary>
    Task<Game?> GetGameAsync(Guid id, CancellationToken cancellationToken);

    void Add(Platform platform);

    void Add(Genre genre);

    void Add(Game game);

    /// <exception cref="DuplicateKeyException">A unique index was violated, e.g. by a concurrent import.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Discards tracked changes so an operation can be retried from scratch.</summary>
    void Reset();
}

public sealed class DuplicateKeyException : Exception
{
    public DuplicateKeyException()
    {
    }

    public DuplicateKeyException(string message)
        : base(message)
    {
    }

    public DuplicateKeyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

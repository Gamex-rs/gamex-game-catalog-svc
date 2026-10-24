namespace Gamex.GameCatalog.Application.Rawg;

public sealed record RawgRef(string Name, string Slug);

/// <summary>A game as returned by RAWG. <see cref="Description"/> is only present on detail lookups.</summary>
public sealed record RawgGame(
    int Id,
    string Slug,
    string Name,
    string? Description,
    DateOnly? Released,
    short? Metacritic,
    string? BackgroundImage,
    IReadOnlyList<RawgRef> Platforms,
    IReadOnlyList<RawgRef> Genres);

public interface IRawgClient
{
    /// <summary>False when no API key is configured; callers then use local data only.</summary>
    bool IsEnabled { get; }

    /// <exception cref="RawgUnavailableException">RAWG failed or timed out.</exception>
    Task<IReadOnlyList<RawgGame>> SearchAsync(string term, int pageSize, CancellationToken cancellationToken);

    /// <param name="idOrSlug">RAWG numeric id or slug.</param>
    /// <returns>The game with description, or <c>null</c> if RAWG does not know it.</returns>
    /// <exception cref="RawgUnavailableException">RAWG failed or timed out.</exception>
    Task<RawgGame?> GetGameAsync(string idOrSlug, CancellationToken cancellationToken);
}

public sealed class RawgUnavailableException : Exception
{
    public RawgUnavailableException()
    {
    }

    public RawgUnavailableException(string message)
        : base(message)
    {
    }

    public RawgUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

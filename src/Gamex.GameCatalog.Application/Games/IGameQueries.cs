namespace Gamex.GameCatalog.Application.Games;

public interface IGameQueries
{
    Task<PagedResult<GameSummaryDto>> ListAsync(GameListQuery query, CancellationToken cancellationToken);

    Task<GameDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Local title search: titles starting with <paramref name="term"/> first, then other matches.</summary>
    Task<IReadOnlyList<GameSummaryDto>> SearchAsync(string term, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlatformDto>> ListPlatformsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<GenreDto>> ListGenresAsync(CancellationToken cancellationToken);
}

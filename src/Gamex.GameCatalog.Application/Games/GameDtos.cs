namespace Gamex.GameCatalog.Application.Games;

public sealed record PlatformDto(Guid Id, string Name, string Slug);

public sealed record GenreDto(Guid Id, string Name, string Slug);

public sealed record GameStatsDto(
    uint OwnedCount,
    uint WishlistCount,
    uint ForExchangeCount,
    uint ForSaleCount,
    uint WantToBuyCount);

/// <summary>Game card in lists and search results.</summary>
public sealed record GameSummaryDto(
    Guid Id,
    string Title,
    string? CoverImageUrl,
    DateOnly? ReleaseDate,
    short? Metacritic,
    IReadOnlyList<PlatformDto> Platforms,
    GameStatsDto Stats);

public sealed record GameDetailDto(
    Guid Id,
    string Title,
    string? Description,
    string? CoverImageUrl,
    DateOnly? ReleaseDate,
    short? Metacritic,
    string? RawgSlug,
    IReadOnlyList<PlatformDto> Platforms,
    IReadOnlyList<GenreDto> Genres,
    GameStatsDto Stats);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

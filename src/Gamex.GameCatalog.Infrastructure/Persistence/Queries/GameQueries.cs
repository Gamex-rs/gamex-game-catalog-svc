using System.Linq.Expressions;
using Gamex.GameCatalog.Application.Games;
using Gamex.GameCatalog.Domain.Games;
using Microsoft.EntityFrameworkCore;

namespace Gamex.GameCatalog.Infrastructure.Persistence.Queries;

internal sealed class GameQueries(CatalogDbContext db) : IGameQueries
{
    private static readonly Expression<Func<Game, GameSummaryDto>> ToSummary = g => new GameSummaryDto(
        g.Id,
        g.Title,
        g.CoverImageUrl,
        g.ReleaseDate,
        g.Metacritic,
        g.Platforms.OrderBy(p => p.Name).Select(p => new PlatformDto(p.Id, p.Name, p.Slug)).ToList(),
        new GameStatsDto(
            g.Stats.OwnedCount,
            g.Stats.WishlistCount,
            g.Stats.ForExchangeCount,
            g.Stats.ForSaleCount,
            g.Stats.WantToBuyCount));

    public async Task<PagedResult<GameSummaryDto>> ListAsync(GameListQuery query, CancellationToken cancellationToken)
    {
        var games = db.Games.AsNoTracking();

        if (query.Q is not null)
        {
            games = games.Where(g => g.Title.Contains(query.Q));
        }

        if (query.PlatformIds.Count > 0)
        {
            games = games.Where(g => g.Platforms.Any(p => query.PlatformIds.Contains(p.Id)));
        }

        if (query.GenreId is { } genreId)
        {
            games = games.Where(g => g.Genres.Any(x => x.Id == genreId));
        }

        var total = await games.CountAsync(cancellationToken);

        var items = await Sort(games, query.SortBy, query.SortDirection)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);

        return new PagedResult<GameSummaryDto>(items, query.Page, query.PageSize, total);
    }

    public Task<GameDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Games.AsNoTracking()
            .Where(g => g.Id == id)
            .Select(g => new GameDetailDto(
                g.Id,
                g.Title,
                g.Description,
                g.CoverImageUrl,
                g.ReleaseDate,
                g.Metacritic,
                g.RawgSlug,
                g.Platforms.OrderBy(p => p.Name).Select(p => new PlatformDto(p.Id, p.Name, p.Slug)).ToList(),
                g.Genres.OrderBy(x => x.Name).Select(x => new GenreDto(x.Id, x.Name, x.Slug)).ToList(),
                new GameStatsDto(
                    g.Stats.OwnedCount,
                    g.Stats.WishlistCount,
                    g.Stats.ForExchangeCount,
                    g.Stats.ForSaleCount,
                    g.Stats.WantToBuyCount)))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<GameSummaryDto>> SearchAsync(string term, int limit, CancellationToken cancellationToken)
    {
        // StartsWith is translated case-sensitively by MySql.EntityFrameworkCore; LIKE uses the column's case-insensitive collation.
        var prefix = EscapeLike(term) + "%";

        return await db.Games.AsNoTracking()
            .Where(g => g.Title.Contains(term))
            .OrderBy(g => EF.Functions.Like(g.Title, prefix) ? 0 : 1)
            .ThenBy(g => g.Title)
            .Take(limit)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Escapes LIKE wildcards using MySQL's default escape character (backslash).</summary>
    internal static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    public async Task<IReadOnlyList<PlatformDto>> ListPlatformsAsync(CancellationToken cancellationToken) =>
        await db.Platforms.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new PlatformDto(p.Id, p.Name, p.Slug))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GenreDto>> ListGenresAsync(CancellationToken cancellationToken) =>
        await db.Genres.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GenreDto(g.Id, g.Name, g.Slug))
            .ToListAsync(cancellationToken);

    // Id is the final tie-breaker so paging is stable when sort values repeat.
    private static IOrderedQueryable<Game> Sort(IQueryable<Game> games, GameSortBy sortBy, SortDirection direction)
    {
        var desc = direction == SortDirection.Desc;
        var sorted = sortBy switch
        {
            GameSortBy.ReleaseDate => desc ? games.OrderByDescending(g => g.ReleaseDate) : games.OrderBy(g => g.ReleaseDate),
            GameSortBy.Metacritic => desc ? games.OrderByDescending(g => g.Metacritic) : games.OrderBy(g => g.Metacritic),
            GameSortBy.OwnedCount => desc ? games.OrderByDescending(g => g.Stats.OwnedCount) : games.OrderBy(g => g.Stats.OwnedCount),
            _ => desc ? games.OrderByDescending(g => g.Title) : games.OrderBy(g => g.Title),
        };
        return sorted.ThenBy(g => g.Id);
    }
}

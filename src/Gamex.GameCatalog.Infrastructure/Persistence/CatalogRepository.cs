using Gamex.GameCatalog.Application.Abstractions;
using Gamex.GameCatalog.Domain.Games;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Gamex.GameCatalog.Infrastructure.Persistence;

internal sealed class CatalogRepository(CatalogDbContext db) : ICatalogRepository
{
    private const int MySqlDuplicateEntry = 1062;

    public Task<List<Platform>> GetPlatformsBySlugAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken) =>
        db.Platforms.Where(p => slugs.Contains(p.Slug)).ToListAsync(cancellationToken);

    public Task<List<Genre>> GetGenresBySlugAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken) =>
        db.Genres.Where(g => slugs.Contains(g.Slug)).ToListAsync(cancellationToken);

    public Task<List<Platform>> GetPlatformsByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.Platforms.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);

    public Task<List<Genre>> GetGenresByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.Genres.Where(g => ids.Contains(g.Id)).ToListAsync(cancellationToken);

    public Task<List<Game>> GetGamesByRawgIdAsync(IReadOnlyCollection<int> rawgIds, CancellationToken cancellationToken) =>
        db.Games
            .Include(g => g.Platforms)
            .Include(g => g.Genres)
            .Where(g => g.RawgId != null && rawgIds.Contains(g.RawgId.Value))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public Task<Game?> GetGameAsync(Guid id, CancellationToken cancellationToken) =>
        db.Games
            .Include(g => g.Platforms)
            .Include(g => g.Genres)
            .AsSplitQuery()
            .SingleOrDefaultAsync(g => g.Id == id, cancellationToken);

    public void Add(Platform platform) => db.Platforms.Add(platform);

    public void Add(Genre genre) => db.Genres.Add(genre);

    public void Add(Game game) => db.Games.Add(game);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: MySqlDuplicateEntry })
        {
            throw new DuplicateKeyException("A unique key was violated.", ex);
        }
    }

    public void Reset() => db.ChangeTracker.Clear();
}

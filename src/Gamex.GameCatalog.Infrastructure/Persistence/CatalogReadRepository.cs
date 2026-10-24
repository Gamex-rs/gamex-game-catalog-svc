using Gamex.GameCatalog.Application.Abstractions;
using Gamex.GameCatalog.Domain.Games;
using Microsoft.EntityFrameworkCore;

namespace Gamex.GameCatalog.Infrastructure.Persistence;

internal sealed class CatalogReadRepository(CatalogDbContext db) : ICatalogReadRepository
{
    public Task<List<Platform>> GetAllPlatformsAsync(CancellationToken cancellationToken) =>
        db.Platforms.AsNoTracking().OrderBy(p => p.Slug).ToListAsync(cancellationToken);

    public Task<List<Platform>> GetPlatformsOfGamesAsync(IReadOnlyCollection<Guid> gameIds, CancellationToken cancellationToken) =>
        db.Games.AsNoTracking()
            .Where(g => gameIds.Contains(g.Id))
            .SelectMany(g => g.Platforms)
            .Distinct()
            .ToListAsync(cancellationToken);

    public Task<List<Game>> GetGamesBatchAsync(int skip, int take, CancellationToken cancellationToken) =>
        db.Games.AsNoTracking()
            .Include(g => g.Platforms)
            .OrderBy(g => g.Id)
            .Skip(skip)
            .Take(take)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public Task<List<int>> GetRefreshCandidatesAsync(int take, CancellationToken cancellationToken) =>
        db.Games.AsNoTracking()
            .Where(g => g.RawgId != null
                        && (g.Stats.ForExchangeCount + g.Stats.ForSaleCount > 0 || g.CoverImageUrl == null))
            // MySQL sorts NULL first in ascending order, so never-refreshed games come first.
            .OrderBy(g => g.RefreshedAt)
            .ThenBy(g => g.Id)
            .Select(g => g.RawgId!.Value)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task MarkRefreshedAsync(int rawgId, DateTime refreshedAt, CancellationToken cancellationToken) =>
        db.Games
            .Where(g => g.RawgId == rawgId)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.RefreshedAt, Domain.Timestamps.ToStoragePrecision(refreshedAt)), cancellationToken);

    public Task<List<Game>> GetGamesByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.Games.AsNoTracking()
            .Include(g => g.Platforms)
            .Where(g => ids.Contains(g.Id))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
}

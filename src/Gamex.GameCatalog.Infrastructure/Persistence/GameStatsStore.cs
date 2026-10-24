using Gamex.GameCatalog.Application.Stats;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Gamex.GameCatalog.Infrastructure.Persistence;

internal sealed class GameStatsStore(CatalogDbContext db) : IGameStatsStore
{
    private const int MySqlDuplicateEntry = 1062;

    public async Task<StatsUpdateOutcome> ApplyAsync(string messageId, Guid gameId, StatsDelta delta, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO processed_library_events (message_id, processed_at) VALUES ({messageId}, UTC_TIMESTAMP(6))",
                cancellationToken);
        }
        catch (MySqlException ex) when (ex.Number == MySqlDuplicateEntry)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StatsUpdateOutcome.Duplicate;
        }

        // Counts are INT UNSIGNED: cast to SIGNED first, or 0 + (-1) fails with "out of range" before GREATEST runs.
        // GREATEST(0, ...) guards against out-of-order delivery (a removal arriving before its add).
        var updated = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE game_stats SET
              owned_count        = GREATEST(0, CAST(owned_count AS SIGNED) + {delta.Owned}),
              wishlist_count     = GREATEST(0, CAST(wishlist_count AS SIGNED) + {delta.Wishlist}),
              for_exchange_count = GREATEST(0, CAST(for_exchange_count AS SIGNED) + {delta.ForExchange}),
              for_sale_count     = GREATEST(0, CAST(for_sale_count AS SIGNED) + {delta.ForSale}),
              want_to_buy_count  = GREATEST(0, CAST(want_to_buy_count AS SIGNED) + {delta.WantToBuy}),
              updated_at         = UTC_TIMESTAMP()
            WHERE game_id = {gameId.ToString()}
            """,
            cancellationToken);

        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StatsUpdateOutcome.GameNotFound;
        }

        await transaction.CommitAsync(cancellationToken);
        return StatsUpdateOutcome.Applied;
    }

    public Task<int> PurgeProcessedBeforeAsync(DateTime cutoff, CancellationToken cancellationToken) =>
        db.ProcessedLibraryEvents.Where(e => e.ProcessedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
}

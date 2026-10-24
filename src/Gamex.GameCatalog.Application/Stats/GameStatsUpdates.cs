namespace Gamex.GameCatalog.Application.Stats;

/// <summary>Per-status change of the number of users, from <c>library.game.status.changed</c>.</summary>
public sealed record StatsDelta(int Owned, int Wishlist, int ForExchange, int ForSale, int WantToBuy)
{
    private IEnumerable<int> Values => [Owned, Wishlist, ForExchange, ForSale, WantToBuy];

    public bool IsZero => Values.All(v => v == 0);

    /// <summary>The User Library Service computes deltas per user, so each value is -1, 0 or +1.</summary>
    public bool IsValid => Values.All(v => v is >= -1 and <= 1);
}

public enum StatsUpdateOutcome
{
    Applied,

    /// <summary>The message was already applied (SQS delivers at least once).</summary>
    Duplicate,

    /// <summary>The game is not in the catalog (yet); the message should be retried.</summary>
    GameNotFound,
}

public interface IGameStatsStore
{
    /// <summary>Atomically applies <paramref name="delta"/> (counts never go below 0) and records <paramref name="messageId"/> as processed.</summary>
    Task<StatsUpdateOutcome> ApplyAsync(string messageId, Guid gameId, StatsDelta delta, CancellationToken cancellationToken);

    /// <returns>Number of deleted idempotency records.</returns>
    Task<int> PurgeProcessedBeforeAsync(DateTime cutoff, CancellationToken cancellationToken);
}

namespace Gamex.GameCatalog.Domain.Games;

/// <summary>Number of users (not copies) per library status for one game.</summary>
public sealed class GameStats
{
    private GameStats()
    {
    }

    public Guid GameId { get; private set; }
    public uint OwnedCount { get; private set; }
    public uint WishlistCount { get; private set; }
    public uint ForExchangeCount { get; private set; }
    public uint ForSaleCount { get; private set; }
    public uint WantToBuyCount { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    internal static GameStats CreateFor(Guid gameId, DateTime now) => new()
    {
        GameId = gameId,
        UpdatedAt = now,
    };
}

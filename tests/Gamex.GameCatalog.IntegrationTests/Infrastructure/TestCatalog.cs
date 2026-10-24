using Gamex.GameCatalog.Domain.Games;
using Gamex.GameCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamex.GameCatalog.IntegrationTests.Infrastructure;

/// <summary>Fixed dataset seeded once per test run. Read-only tests rely on these exact values.</summary>
public sealed class TestCatalog
{
    private static readonly DateTime Now = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);

    public Platform Ps5 { get; } = Platform.Create("PlayStation 5", "playstation5", Now);
    public Platform Pc { get; } = Platform.Create("PC", "pc", Now);
    public Platform Switch { get; } = Platform.Create("Nintendo Switch", "nintendo-switch", Now);

    public Genre Action { get; } = Genre.Create("Action", "action");
    public Genre Rpg { get; } = Genre.Create("RPG", "role-playing-games-rpg");

    public Game EldenRing { get; private set; } = null!;
    public Game Nightreign { get; private set; } = null!;
    public Game Zelda { get; private set; } = null!;
    public Game Hades { get; private set; } = null!;
    public Game Stardew { get; private set; } = null!;

    public IReadOnlyList<Game> AllGames => [EldenRing, Nightreign, Zelda, Hades, Stardew];

    public async Task SeedAsync(CatalogDbContext db, CancellationToken ct)
    {
        EldenRing = Create("Elden Ring", 326243, new DateOnly(2022, 2, 25), 96, [Ps5, Pc], [Action, Rpg]);
        Nightreign = Create("Elden Ring Nightreign", 1001, new DateOnly(2025, 5, 30), 77, [Ps5, Pc], [Action]);
        Zelda = Create("The Legend of Zelda: Tears of the Kingdom", 1002, new DateOnly(2023, 5, 12), 96, [Switch], [Action]);
        Hades = Create("Hades", 1003, new DateOnly(2020, 9, 17), 93, [Pc, Switch, Ps5], [Action, Rpg]);
        Stardew = Create("Stardew Valley", 1004, new DateOnly(2016, 2, 26), null, [Pc, Switch], [Rpg]);

        db.AddRange(Ps5, Pc, Switch, Action, Rpg);
        db.AddRange(AllGames);
        await db.SaveChangesAsync(ct);

        await SetOwnedCount(db, EldenRing, 142, ct);
        await SetOwnedCount(db, Hades, 300, ct);
        await SetOwnedCount(db, Zelda, 57, ct);
    }

    private static Game Create(string title, int rawgId, DateOnly released, short? metacritic, Platform[] platforms, Genre[] genres) =>
        Game.Create(
            new GameMetadata(rawgId, $"slug-{rawgId}", title, $"About {title}", $"https://media.gamex.rs/game-covers/{rawgId}.jpg", released, metacritic),
            platforms,
            genres,
            Now);

    // Stats are only changed by the SQS consumer in production; set them directly for sort tests.
    private static async Task SetOwnedCount(CatalogDbContext db, Game game, uint count, CancellationToken ct) =>
        await db.GameStats.Where(s => s.GameId == game.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.OwnedCount, count), ct);
}

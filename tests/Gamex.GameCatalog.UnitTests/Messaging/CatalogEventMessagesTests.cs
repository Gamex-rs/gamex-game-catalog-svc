using System.Text.Json;
using Gamex.GameCatalog.Domain;
using Gamex.GameCatalog.Domain.Games;
using Gamex.GameCatalog.Infrastructure.Messaging;

namespace Gamex.GameCatalog.UnitTests.Messaging;

public sealed class CatalogEventMessagesTests
{
    // 7 fractional digits, like DateTime.UtcNow; storage keeps 6.
    private static readonly DateTime Now = new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc).AddTicks(1234567);

    [Fact]
    public void Timestamps_TruncateToMicroseconds()
    {
        var stored = Timestamps.ToStoragePrecision(Now);

        stored.Ticks.ShouldBe(Now.Ticks - 7);
        stored.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void PlatformMessage_MatchesSpec()
    {
        var platform = Platform.Create("PlayStation 5", "playstation5", Now);

        var json = JsonSerializer.Serialize(CatalogEventMessages.From(platform), CatalogEventMessages.JsonOptions);

        json.ShouldBe(
            $$"""{"eventType":"catalog.platform.upserted","platformId":"{{platform.Id}}","name":"PlayStation 5","slug":"playstation5","updatedAt":"2026-10-24T10:00:00.123456Z"}""");
    }

    [Fact]
    public void GameMessage_MatchesSpec()
    {
        var ps5 = Platform.Create("PlayStation 5", "playstation5", Now);
        var pc = Platform.Create("PC", "pc", Now);
        var game = Game.Create(
            new GameMetadata(326243, "elden-ring", "Elden Ring", "Not replicated", "https://media.gamex.rs/c.jpg", new DateOnly(2022, 2, 25), 96),
            [ps5, pc],
            [],
            Now);
        var platformIds = new[] { ps5.Id, pc.Id }.Order().ToArray();

        var json = JsonSerializer.Serialize(CatalogEventMessages.From(game), CatalogEventMessages.JsonOptions);

        json.ShouldBe(
            $$"""{"eventType":"catalog.game.upserted","gameId":"{{game.Id}}","title":"Elden Ring","coverImageUrl":"https://media.gamex.rs/c.jpg","releaseDate":"2022-02-25","metacritic":96,"platformIds":["{{platformIds[0]}}","{{platformIds[1]}}"],"updatedAt":"2026-10-24T10:00:00.123456Z"}""");
    }

    [Fact]
    public void GameMessage_NullFieldsAreWrittenAsNull()
    {
        var game = Game.Create(new GameMetadata(null, null, "Homebrew", null, null, null, null), [], [], Now);

        var json = JsonSerializer.Serialize(CatalogEventMessages.From(game), CatalogEventMessages.JsonOptions);

        json.ShouldContain("\"coverImageUrl\":null");
        json.ShouldContain("\"releaseDate\":null");
        json.ShouldContain("\"metacritic\":null");
        json.ShouldContain("\"platformIds\":[]");
    }
}

using System.Net.Http.Json;
using Gamex.GameCatalog.Application.Games;
using Gamex.GameCatalog.Application.Rawg;
using Gamex.GameCatalog.Infrastructure.Persistence;
using Gamex.GameCatalog.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gamex.GameCatalog.IntegrationTests;

/// <summary>Each test uses its own search term and RAWG ids so tests can share one database.</summary>
[Collection(ImportApiFixture.Name)]
public sealed class SearchImportTests(ImportApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RawgGame Game(int id, string name, string? image = null, params (string Name, string Slug)[] platforms) => new(
        id,
        $"rawg-{id}",
        name,
        null,
        new DateOnly(2023, 5, 12),
        96,
        image,
        platforms.Select(p => new RawgRef(p.Name, p.Slug)).ToList(),
        [new RawgRef("Adventure", "adventure")]);

    private async Task<List<GameSummaryDto>> SearchAsync(string term) =>
        (await _client.GetFromJsonAsync<List<GameSummaryDto>>($"/api/v1/games/search?q={Uri.EscapeDataString(term)}", Ct))
        .ShouldNotBeNull();

    [Fact]
    public async Task Search_NoLocalResults_ImportsRawgGamesAndReturnsThem()
    {
        factory.Rawg.Results["zelda"] =
        [
            Game(2001, "The Legend of Zelda: Breath of the Wild", null, ("Nintendo Switch", "nintendo-switch"), ("Wii U", "wii-u")),
            Game(2002, "Zelda II: The Adventure of Link", null, ("NES", "nes")),
        ];

        var results = await SearchAsync("zelda");

        results.Select(r => r.Title).ShouldBe(["Zelda II: The Adventure of Link", "The Legend of Zelda: Breath of the Wild"]);
        results[1].Platforms.Select(p => p.Slug).ShouldBe(["nintendo-switch", "wii-u"]);
        results[1].Stats.OwnedCount.ShouldBe(0u);

        // Persisted: the detail endpoint serves the imported game.
        var detail = await _client.GetFromJsonAsync<GameDetailDto>($"/api/v1/games/{results[1].Id}", Ct);
        detail.ShouldNotBeNull().RawgSlug.ShouldBe("rawg-2001");
        detail.Genres.Select(g => g.Slug).ShouldBe(["adventure"]);
    }

    [Fact]
    public async Task Search_ImportPublishesPlatformsBeforeGames()
    {
        factory.Rawg.Results["metroid"] = [Game(2101, "Metroid Dread", null, ("Nintendo Switch 2", "nintendo-switch-2"))];

        var results = await SearchAsync("metroid");
        var gameId = results.ShouldHaveSingleItem().Id;
        var platformId = results[0].Platforms.ShouldHaveSingleItem().Id;

        var published = factory.Events.Published.ToList();
        var platformEvent = published.FindIndex(e => e.Id == platformId);
        var gameEvent = published.FindIndex(e => e.Id == gameId);
        platformEvent.ShouldBeGreaterThanOrEqualTo(0);
        published[platformEvent].Type.ShouldBe("catalog.platform.upserted");
        gameEvent.ShouldBeGreaterThan(platformEvent);
        published[gameEvent].Type.ShouldBe("catalog.game.upserted");
    }

    [Fact]
    public async Task Search_SameTermAgain_ReusesImportedGames()
    {
        factory.Rawg.Results["kirby"] = [Game(2201, "Kirby and the Forgotten Land", null, ("Nintendo Switch", "nintendo-switch"))];

        var first = await SearchAsync("kirby");
        var second = await SearchAsync("kirby");

        second.Select(g => g.Id).ShouldBe(first.Select(g => g.Id));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        (await db.Games.CountAsync(g => g.RawgId == 2201, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Search_RawgUnavailable_ReturnsLocalResultsWith200()
    {
        factory.Rawg.FailingTerms["outage"] = true;

        var results = await SearchAsync("outage");

        results.ShouldBeEmpty();
        factory.Rawg.Searches.ShouldContain("outage");
    }

    [Fact]
    public async Task Search_ImportedCover_IsCopiedToS3AndAttached()
    {
        factory.Rawg.Results["celeste"] = [Game(2301, "Celeste", "https://media.rawg.io/media/games/celeste.jpg", ("PC", "pc"))];

        var gameId = (await SearchAsync("celeste")).ShouldHaveSingleItem().Id;

        var cover = await WaitForCoverAsync(gameId);
        cover.ShouldStartWith($"{ImportApiFactory.CdnBaseUrl}/{ImportApiFactory.MediaPrefix}game-covers/2301.jpg?v=");

        using var s3 = factory.CreateS3Client();
        using var stored = await s3.GetObjectAsync(ImportApiFactory.Bucket, $"{ImportApiFactory.MediaPrefix}game-covers/2301.jpg", Ct);
        stored.Headers.ContentType.ShouldBe("image/jpeg");
        using var bytes = new MemoryStream();
        await stored.ResponseStream.CopyToAsync(bytes, Ct);
        bytes.ToArray().ShouldBe(ImportApiFactory.CoverBytes);

        // Attaching the cover changes a replicated field, so a second game event follows the import event.
        factory.Events.Published.Count(e => e.Id == gameId).ShouldBe(2);
    }

    private async Task<string> WaitForCoverAsync(Guid gameId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var game = await _client.GetFromJsonAsync<GameDetailDto>($"/api/v1/games/{gameId}", Ct);
            if (game?.CoverImageUrl is { } url)
            {
                return url;
            }

            await Task.Delay(200, Ct);
        }

        throw new TimeoutException($"Cover for game {gameId} was not attached within 15s.");
    }
}

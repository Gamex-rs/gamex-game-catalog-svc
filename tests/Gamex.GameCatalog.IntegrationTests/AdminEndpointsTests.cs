using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Gamex.GameCatalog.Application.Games;
using Gamex.GameCatalog.Application.Rawg;
using Gamex.GameCatalog.Infrastructure.Persistence;
using Gamex.GameCatalog.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gamex.GameCatalog.IntegrationTests;

[Collection(ImportApiFixture.Name)]
public sealed class AdminEndpointsTests(ImportApiFactory factory)
{
    private readonly HttpClient _admin = factory.CreateClient("admin");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RawgGame Detail(int id, string slug, string name, string description, params (string Name, string Slug)[] platforms) => new(
        id, slug, name, description, new DateOnly(2017, 2, 24), 87, null,
        platforms.Select(p => new RawgRef(p.Name, p.Slug)).ToList(),
        [new RawgRef("Indie", "indie")]);

    private async Task<GameDetailDto> SyncAsync(RawgGame detail)
    {
        factory.Rawg.Details[detail.Slug] = detail;
        var response = await _admin.PostAsJsonAsync("/api/v1/games/sync", new { rawgSlug = detail.Slug }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>(Ct)).ShouldNotBeNull();
    }

    public static TheoryData<string, string> AdminRoutes() => new()
    {
        { "POST", "/api/v1/games/sync" },
        { "PUT", $"/api/v1/games/{Guid.NewGuid()}" },
        { "POST", "/api/v1/admin/catalog-events/replay" },
    };

    private static HttpRequestMessage Request(string method, string path, string? token) => new(new HttpMethod(method), path)
    {
        Content = JsonContent.Create(new { }),
        Headers = { Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token) },
    };

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task AdminRoute_WithoutToken_Returns401(string method, string path)
    {
        var response = await factory.CreateClient().SendAsync(Request(method, path, token: null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task AdminRoute_WithUserRole_Returns403(string method, string path)
    {
        var response = await factory.CreateClient().SendAsync(Request(method, path, ApiFactoryBase.CreateToken("user")), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminRoute_TokenSignedWithOtherKey_Returns401()
    {
        using var otherKey = RSA.Create(2048);

        var response = await factory.CreateClient().SendAsync(
            Request("POST", "/api/v1/admin/catalog-events/replay", ApiFactoryBase.CreateToken("admin", otherKey)), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminRoute_ExpiredToken_Returns401()
    {
        var expired = ApiFactoryBase.CreateToken("admin", expires: DateTime.UtcNow.AddMinutes(-5));

        var response = await factory.CreateClient().SendAsync(Request("POST", "/api/v1/admin/catalog-events/replay", expired), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PublicRoutes_StayAnonymous()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/api/v1/platforms", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sync_ImportsGameWithDescription()
    {
        var game = await SyncAsync(Detail(3001, "hollow-knight", "Hollow Knight", "Forge your own path.", ("PC", "pc")));

        game.Title.ShouldBe("Hollow Knight");
        game.Description.ShouldBe("Forge your own path.");
        game.RawgSlug.ShouldBe("hollow-knight");
        game.Platforms.Select(p => p.Slug).ShouldBe(["pc"]);
        game.Genres.Select(g => g.Slug).ShouldBe(["indie"]);
    }

    [Fact]
    public async Task Sync_UnknownSlug_Returns404()
    {
        var response = await _admin.PostAsJsonAsync("/api/v1/games/sync", new { rawgSlug = "does-not-exist" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        problem.ShouldNotBeNull().Title.ShouldBe("RAWG game not found");
    }

    [Fact]
    public async Task Sync_RawgDown_Returns502()
    {
        factory.Rawg.FailingTerms["sync-outage"] = true;

        var response = await _admin.PostAsJsonAsync("/api/v1/games/sync", new { rawgSlug = "sync-outage" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Sync_MissingSlug_Returns400()
    {
        var response = await _admin.PostAsJsonAsync("/api/v1/games/sync", new { }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ChangesGameAndPublishesEvent()
    {
        var game = await SyncAsync(Detail(3101, "dead-cells", "Dead Cells", "Roguevania.", ("PC", "pc"), ("Xbox One", "xbox-one")));
        var pc = game.Platforms.Single(p => p.Slug == "pc");
        var eventsBefore = factory.Events.Published.Count(e => e.Id == game.Id);

        var response = await _admin.PutAsJsonAsync($"/api/v1/games/{game.Id}", new
        {
            title = "Dead Cells: Return to Castlevania",
            description = "Edited.",
            coverImageUrl = "https://media.gamex.test/manual/dead-cells.jpg",
            releaseDate = "2018-08-07",
            metacritic = 89,
            platformIds = new[] { pc.Id },
            genreIds = game.Genres.Select(g => g.Id).ToArray(),
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<GameDetailDto>(Ct)).ShouldNotBeNull();
        updated.Title.ShouldBe("Dead Cells: Return to Castlevania");
        updated.Description.ShouldBe("Edited.");
        updated.CoverImageUrl.ShouldBe("https://media.gamex.test/manual/dead-cells.jpg");
        updated.ReleaseDate.ShouldBe(new DateOnly(2018, 8, 7));
        updated.Metacritic.ShouldBe((short)89);
        updated.Platforms.Select(p => p.Slug).ShouldBe(["pc"]);
        updated.RawgSlug.ShouldBe("dead-cells");
        factory.Events.Published.Count(e => e.Id == game.Id).ShouldBe(eventsBefore + 1);
    }

    [Fact]
    public async Task Update_UnknownPlatform_Returns400()
    {
        var game = await SyncAsync(Detail(3201, "inside", "Inside", "Hunted and alone.", ("PC", "pc")));

        var response = await _admin.PutAsJsonAsync($"/api/v1/games/{game.Id}",
            new { title = "Inside", platformIds = new[] { Guid.NewGuid() } }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);
        problem.ShouldNotBeNull().Errors.Keys.ShouldContain("platformIds");
    }

    [Fact]
    public async Task Update_UnknownGame_Returns404()
    {
        var response = await _admin.PutAsJsonAsync($"/api/v1/games/{Guid.NewGuid()}", new { title = "X" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Replay_All_PublishesEveryPlatformBeforeEveryGame()
    {
        await SyncAsync(Detail(3301, "limbo", "Limbo", "Uncertain of his sister's fate.", ("PlayStation 3", "playstation3")));
        int platformCount, gameCount;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            platformCount = await db.Platforms.CountAsync(Ct);
            gameCount = await db.Games.CountAsync(Ct);
        }

        var marker = factory.Events.Published.Count;

        var response = await _admin.PostAsync(new Uri("/api/v1/admin/catalog-events/replay", UriKind.Relative), null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadFromJsonAsync<JobAccepted>(Ct)).ShouldNotBeNull().JobId.ShouldNotBe(Guid.Empty);

        var replayed = await WaitForEventsAsync(marker, platformCount + gameCount);
        replayed.Take(platformCount).ShouldAllBe(e => e.Type == "catalog.platform.upserted");
        replayed.Skip(platformCount).ShouldAllBe(e => e.Type == "catalog.game.upserted");
        replayed.Skip(platformCount).Select(e => e.Id).Distinct().Count().ShouldBe(gameCount);
    }

    [Fact]
    public async Task Replay_SelectedGames_PublishesOnlyThose()
    {
        var game = await SyncAsync(Detail(3401, "braid", "Braid", "Time and forgiveness.", ("Xbox 360", "xbox360")));
        var marker = factory.Events.Published.Count;

        var response = await _admin.PostAsync(new Uri($"/api/v1/admin/catalog-events/replay?gameIds={game.Id}", UriKind.Relative), null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var replayed = await WaitForEventsAsync(marker, 2);
        replayed.ShouldBe([("catalog.platform.upserted", game.Platforms.Single().Id), ("catalog.game.upserted", game.Id)]);
    }

    [Fact]
    public async Task Replay_InvalidGameId_Returns400()
    {
        var response = await _admin.PostAsync(new Uri("/api/v1/admin/catalog-events/replay?gameIds=abc", UriKind.Relative), null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<List<(string Type, Guid Id)>> WaitForEventsAsync(int skip, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var events = factory.Events.Published.Skip(skip).ToList();
            if (events.Count >= count)
            {
                return events;
            }

            await Task.Delay(100, Ct);
        }

        throw new TimeoutException($"Expected {count} events after replay.");
    }

    private sealed record JobAccepted(Guid JobId);
}

using System.Net;
using System.Net.Http.Json;
using Gamex.GameCatalog.Application.Games;
using Gamex.GameCatalog.Application.Rawg;
using Gamex.GameCatalog.Application.Refresh;
using Gamex.GameCatalog.Application.Stats;
using Gamex.GameCatalog.Infrastructure.Jobs;
using Gamex.GameCatalog.Infrastructure.Persistence;
using Gamex.GameCatalog.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gamex.GameCatalog.IntegrationTests;

[Collection(ImportApiFixture.Name)]
public sealed class NightlyRefreshTests(ImportApiFactory factory)
{
    private readonly HttpClient _admin = factory.CreateClient("admin");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RawgGame Detail(int id, string title, string description, string? image = "https://media.rawg.io/x.jpg") => new(
        id, $"nightly-{id}", title, description, new DateOnly(2020, 1, 1), 70, image, [new RawgRef("PC", "pc")], []);

    /// <summary>Imports a game via admin sync; the refresh looks details up by RAWG id.</summary>
    private async Task<GameDetailDto> SyncAsync(RawgGame detail)
    {
        factory.Rawg.Details[detail.Slug] = detail;
        factory.Rawg.Details[detail.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)] = detail;
        var response = await _admin.PostAsJsonAsync("/api/v1/games/sync", new { rawgSlug = detail.Slug }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>(Ct)).ShouldNotBeNull();
    }

    private async Task PutOnSaleAsync(Guid gameId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGameStatsStore>();
        (await store.ApplyAsync($"nightly-{gameId}", gameId, new StatsDelta(0, 0, 0, 1, 0), Ct)).ShouldBe(StatsUpdateOutcome.Applied);
    }

    private async Task<(string Title, string? Description, DateTime? RefreshedAt)> LoadAsync(Guid gameId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return await db.Games.AsNoTracking().Where(g => g.Id == gameId)
            .Select(g => new ValueTuple<string, string?, DateTime?>(g.Title, g.Description, g.RefreshedAt))
            .SingleAsync(Ct);
    }

    private async Task<NightlyRefreshResult> RunRefreshAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INightlyRefreshService>().RunAsync(Ct);
    }

    private async Task WaitForCoverAsync(Guid gameId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var game = await _admin.GetFromJsonAsync<GameDetailDto>($"/api/v1/games/{gameId}", Ct);
            if (game?.CoverImageUrl is not null)
            {
                return;
            }

            await Task.Delay(200, Ct);
        }

        throw new TimeoutException("Cover not attached.");
    }

    [Fact]
    public async Task Refresh_GameOnSale_IsUpdatedFromRawgAndMarked()
    {
        var game = await SyncAsync(Detail(5001, "Spiritfarer", "Old description."));
        await WaitForCoverAsync(game.Id);
        await PutOnSaleAsync(game.Id);

        factory.Rawg.Details["5001"] = Detail(5001, "Spiritfarer: Farewell Edition", "New description.");
        await RunRefreshAsync();

        var (title, description, refreshedAt) = await LoadAsync(game.Id);
        title.ShouldBe("Spiritfarer: Farewell Edition");
        description.ShouldBe("New description.");
        refreshedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Refresh_GameWithCoverAndNoListings_IsSkipped()
    {
        var game = await SyncAsync(Detail(5101, "Stray", "Lost cat."));
        await WaitForCoverAsync(game.Id);

        factory.Rawg.Details["5101"] = Detail(5101, "Stray (changed)", "Lost cat.");
        await RunRefreshAsync();

        var (title, _, refreshedAt) = await LoadAsync(game.Id);
        title.ShouldBe("Stray");
        refreshedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Refresh_GameWithoutCover_IsRefreshed()
    {
        // No RAWG image, so the game never gets a cover and stays a candidate.
        var game = await SyncAsync(Detail(5201, "Untitled Goose Game", "Honk.", image: null));

        await RunRefreshAsync();

        (await LoadAsync(game.Id)).RefreshedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Refresh_PurgesOldProcessedEvents()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO processed_library_events (message_id, processed_at) VALUES ('old-msg', UTC_TIMESTAMP(6) - INTERVAL 15 DAY), ('new-msg', UTC_TIMESTAMP(6))",
                Ct);
        }

        await RunRefreshAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var remaining = await db.ProcessedLibraryEvents.Select(e => e.MessageId).ToListAsync(Ct);
            remaining.ShouldNotContain("old-msg");
            remaining.ShouldContain("new-msg");
        }
    }

    [Fact]
    public async Task JobLock_OnlyOneHolderAtATime()
    {
        var jobLock = factory.Services.GetRequiredService<MySqlJobLock>();

        await using (var first = await jobLock.TryAcquireAsync("test-lock", Ct))
        {
            first.ShouldNotBeNull();
            (await jobLock.TryAcquireAsync("test-lock", Ct)).ShouldBeNull();
        }

        await using var again = await jobLock.TryAcquireAsync("test-lock", Ct);
        again.ShouldNotBeNull();
    }
}

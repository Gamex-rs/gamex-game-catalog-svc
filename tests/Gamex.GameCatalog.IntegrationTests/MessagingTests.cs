using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Gamex.GameCatalog.Application.Games;
using Gamex.GameCatalog.Application.Rawg;
using Gamex.GameCatalog.Application.Stats;
using Gamex.GameCatalog.Infrastructure.Persistence;
using Gamex.GameCatalog.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gamex.GameCatalog.IntegrationTests;

[Collection(MessagingApiFixture.Name)]
public sealed class MessagingTests(MessagingApiFactory factory) : IDisposable
{
    private readonly HttpClient _admin = factory.CreateClient("admin");
    private readonly HttpClient _anonymous = factory.CreateClient();
    private readonly AmazonSQSClient _sqs = factory.CreateSqsClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _sqs.Dispose();

    private async Task<GameDetailDto> SyncAsync(int rawgId, string slug, params (string Name, string Slug)[] platforms)
    {
        factory.Rawg.Details[slug] = new RawgGame(
            rawgId, slug, $"Game {slug}", "About.", new DateOnly(2021, 1, 1), 80, null,
            platforms.Select(p => new RawgRef(p.Name, p.Slug)).ToList(), []);

        var response = await _admin.PostAsJsonAsync("/api/v1/games/sync", new { rawgSlug = slug }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<GameDetailDto>(Ct)).ShouldNotBeNull();
    }

    private async Task SendLibraryEventAsync(Guid gameId, int owned = 0, int wishlist = 0, int forSale = 0) =>
        await _sqs.SendMessageAsync(factory.LibraryEventsUrl, $$"""
            {"eventType":"library.game.status.changed","gameId":"{{gameId}}","userId":"{{Guid.NewGuid()}}",
             "delta":{"owned":{{owned}},"wishlist":{{wishlist}},"forExchange":0,"forSale":{{forSale}},"wantToBuy":0},
             "occurredAt":"2026-10-24T10:00:00Z"}
            """, Ct);

    private async Task<GameStatsDto> WaitForStatsAsync(Guid gameId, Func<GameStatsDto, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        GameStatsDto? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = (await _anonymous.GetFromJsonAsync<GameDetailDto>($"/api/v1/games/{gameId}", Ct))!.Stats;
            if (condition(last))
            {
                return last;
            }

            await Task.Delay(200, Ct);
        }

        throw new TimeoutException($"Stats condition not met; last: {last}");
    }

    /// <summary>Receives (and deletes) catalog events until <paramref name="done"/> holds for the collected bodies.</summary>
    private async Task<List<JsonElement>> ReceiveCatalogEventsAsync(Func<List<JsonElement>, bool> done)
    {
        var collected = new List<JsonElement>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && !done(collected))
        {
            var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = factory.CatalogEventsUrl,
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 1,
                MessageAttributeNames = ["All"],
            }, Ct);

            foreach (var message in response.Messages ?? [])
            {
                var body = JsonDocument.Parse(message.Body).RootElement.Clone();
                body.GetProperty("eventType").GetString().ShouldBe(message.MessageAttributes["eventType"].StringValue);
                collected.Add(body);
                await _sqs.DeleteMessageAsync(factory.CatalogEventsUrl, message.ReceiptHandle, Ct);
            }
        }

        return collected;
    }

    [Fact]
    public async Task Sync_PublishesPlatformAndGameEventsMatchingTheDatabase()
    {
        var game = await SyncAsync(4001, "ori", ("Xbox Series S/X", "xbox-series-x"));
        var platformId = game.Platforms.Single().Id;

        var events = await ReceiveCatalogEventsAsync(c =>
            c.Any(e => e.TryGetProperty("gameId", out var id) && id.GetGuid() == game.Id));

        var platformEvent = events.Single(e => e.TryGetProperty("platformId", out var id) && id.GetGuid() == platformId);
        platformEvent.GetProperty("eventType").GetString().ShouldBe("catalog.platform.upserted");
        platformEvent.GetProperty("slug").GetString().ShouldBe("xbox-series-x");

        var gameEvent = events.Single(e => e.TryGetProperty("gameId", out var id) && id.GetGuid() == game.Id);
        gameEvent.GetProperty("eventType").GetString().ShouldBe("catalog.game.upserted");
        gameEvent.GetProperty("title").GetString().ShouldBe("Game ori");
        gameEvent.GetProperty("releaseDate").GetString().ShouldBe("2021-01-01");
        gameEvent.GetProperty("metacritic").GetInt32().ShouldBe(80);
        gameEvent.GetProperty("platformIds").EnumerateArray().Select(p => p.GetGuid()).ShouldBe([platformId]);

        // The version in the event must equal the stored version, or consumers would drop later replays as stale.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var stored = await db.Games.AsNoTracking().Where(g => g.Id == game.Id).Select(g => g.UpdatedAt).SingleAsync(Ct);
        gameEvent.GetProperty("updatedAt").GetString()
            .ShouldBe(stored.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task LibraryEvent_UpdatesStats()
    {
        var game = await SyncAsync(4101, "gris", ("PC", "pc"));

        await SendLibraryEventAsync(game.Id, owned: 1, wishlist: 1);
        await SendLibraryEventAsync(game.Id, owned: 1);

        var stats = await WaitForStatsAsync(game.Id, s => s.OwnedCount == 2);
        stats.WishlistCount.ShouldBe(1u);
    }

    [Fact]
    public async Task LibraryEvent_NegativeDeltaAtZero_StaysZero()
    {
        var game = await SyncAsync(4201, "tunic", ("PC", "pc"));

        await SendLibraryEventAsync(game.Id, forSale: -1);
        await SendLibraryEventAsync(game.Id, owned: 1);

        var stats = await WaitForStatsAsync(game.Id, s => s.OwnedCount == 1);
        stats.ForSaleCount.ShouldBe(0u);
    }

    [Fact]
    public async Task StatsStore_SameMessageTwice_AppliesOnce()
    {
        var game = await SyncAsync(4301, "hob", ("PC", "pc"));
        var delta = new StatsDelta(1, 0, 0, 0, 0);

        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGameStatsStore>();
        (await store.ApplyAsync("msg-duplicate-test", game.Id, delta, Ct)).ShouldBe(StatsUpdateOutcome.Applied);
        (await store.ApplyAsync("msg-duplicate-test", game.Id, delta, Ct)).ShouldBe(StatsUpdateOutcome.Duplicate);

        var stats = (await _anonymous.GetFromJsonAsync<GameDetailDto>($"/api/v1/games/{game.Id}", Ct))!.Stats;
        stats.OwnedCount.ShouldBe(1u);
    }

    [Fact]
    public async Task StatsStore_UnknownGame_ReturnsGameNotFoundAndRecordsNothing()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGameStatsStore>();

        var outcome = await store.ApplyAsync("msg-unknown-game", Guid.NewGuid(), new StatsDelta(1, 0, 0, 0, 0), Ct);

        outcome.ShouldBe(StatsUpdateOutcome.GameNotFound);
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        (await db.ProcessedLibraryEvents.AnyAsync(e => e.MessageId == "msg-unknown-game", Ct)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("unknown-game")]
    [InlineData("malformed")]
    public async Task LibraryEvent_Unprocessable_EndsInDlq(string kind)
    {
        var marker = Guid.NewGuid().ToString();
        var body = kind == "malformed"
            ? $"not json {marker}"
            : $$"""
              {"eventType":"library.game.status.changed","gameId":"{{Guid.NewGuid()}}","userId":"{{Guid.NewGuid()}}",
               "delta":{"owned":1,"wishlist":0,"forExchange":0,"forSale":0,"wantToBuy":0},"occurredAt":"2026-10-24T10:00:00Z","marker":"{{marker}}"}
              """;

        await _sqs.SendMessageAsync(factory.LibraryEventsUrl, body, Ct);

        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = factory.LibraryEventsDlqUrl,
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 1,
            }, Ct);

            var match = response.Messages?.FirstOrDefault(m => m.Body.Contains(marker, StringComparison.Ordinal));
            if (match is not null)
            {
                await _sqs.DeleteMessageAsync(factory.LibraryEventsDlqUrl, match.ReceiptHandle, Ct);
                return;
            }
        }

        throw new TimeoutException($"{kind} message did not reach the DLQ.");
    }
}

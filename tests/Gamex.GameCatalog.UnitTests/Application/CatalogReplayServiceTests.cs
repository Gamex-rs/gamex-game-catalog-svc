using Gamex.GameCatalog.Application.Games;
using Gamex.GameCatalog.Domain.Games;
using Gamex.GameCatalog.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gamex.GameCatalog.UnitTests.Application;

public sealed class CatalogReplayServiceTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);

    private readonly FakeCatalogRepository _source = new();
    private readonly FakeCatalogReadRepository _read;
    private readonly RecordingEventPublisher _events = new();
    private readonly Platform _pc = Platform.Create("PC", "pc", T0);
    private readonly Platform _switch = Platform.Create("Switch", "nintendo-switch", T0);

    public CatalogReplayServiceTests()
    {
        _read = new FakeCatalogReadRepository(_source);
        _source.Platforms.AddRange([_pc, _switch]);
    }

    private CatalogReplayService CreateService() => new(_read, _events, NullLogger<CatalogReplayService>.Instance);

    private Game AddGame(int rawgId, params Platform[] platforms)
    {
        var game = Game.Create(new GameMetadata(rawgId, $"g{rawgId}", $"Game {rawgId}", null, null, null, null), platforms, [], T0);
        _source.Games.Add(game);
        return game;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReplayAll_PublishesAllPlatformsThenAllGamesInBatches()
    {
        for (var i = 0; i < CatalogReplayService.BatchSize + 5; i++)
        {
            AddGame(i, _pc);
        }

        await CreateService().ReplayAsync(null, Ct);

        _events.Log.Take(2).ShouldBe(["platform:pc", "platform:nintendo-switch"], ignoreOrder: true);
        _events.Log.Skip(2).Count().ShouldBe(CatalogReplayService.BatchSize + 5);
        _events.Log.Skip(2).ShouldAllBe(e => e.StartsWith("game:", StringComparison.Ordinal));
        _read.BatchRequests.ShouldBe([(0, 100), (100, 100), (105, 100)]);
    }

    [Fact]
    public async Task ReplaySelected_PublishesOnlyThoseGamesAndTheirPlatforms()
    {
        var a = AddGame(1, _switch);
        AddGame(2, _pc);

        await CreateService().ReplayAsync([a.Id, Guid.NewGuid()], Ct);

        _events.Log.ShouldBe(["platform:nintendo-switch", "game:g1"]);
    }

    [Fact]
    public async Task ReplayAll_EmptyCatalog_PublishesOnlyPlatforms()
    {
        await CreateService().ReplayAsync(null, Ct);

        _events.Log.Count.ShouldBe(2);
    }
}

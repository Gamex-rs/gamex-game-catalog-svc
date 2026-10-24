using Gamex.GameCatalog.Application.Refresh;
using Gamex.GameCatalog.Application.Stats;
using Gamex.GameCatalog.Domain.Games;
using Gamex.GameCatalog.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Gamex.GameCatalog.UnitTests.Fakes.TestGames;

namespace Gamex.GameCatalog.UnitTests.Application;

public sealed class NightlyRefreshServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 1, 0, 0, TimeSpan.Zero);

    private readonly FakeCatalogRepository _source = new();
    private readonly FakeCatalogReadRepository _read;
    private readonly FakeRawgClient _rawg = new();
    private readonly RecordingImporter _importer = new();
    private readonly FakeStatsStore _stats = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly NightlyRefreshOptions _options = new() { RequestInterval = TimeSpan.Zero, MaxGames = 10 };

    public NightlyRefreshServiceTests() => _read = new FakeCatalogReadRepository(_source);

    private NightlyRefreshService CreateService() =>
        new(_read, _rawg, _importer, _stats, _options, _time, NullLogger<NightlyRefreshService>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Run_ImportsDetailForEachCandidateAndMarksThemRefreshed()
    {
        _read.Candidates.AddRange([1, 2]);
        _rawg.SearchResults.AddRange([NewRawg(1, "One", description: "d1"), NewRawg(2, "Two", description: "d2")]);

        var result = await CreateService().RunAsync(Ct);

        result.ShouldBe(new NightlyRefreshResult(2, 2, 0, 0, false, 0));
        _importer.Imports.SelectMany(i => i).Select(g => g.Description).ShouldBe(["d1", "d2"]);
        _read.Refreshed.ShouldBe([1, 2]);
    }

    [Fact]
    public async Task Run_RequestsAtMostMaxGames()
    {
        _options.MaxGames = 3;

        await CreateService().RunAsync(Ct);

        _read.CandidateRequests.ShouldBe([3]);
    }

    [Fact]
    public async Task Run_GameGoneFromRawg_IsMarkedRefreshedButNotImported()
    {
        _read.Candidates.Add(99);

        var result = await CreateService().RunAsync(Ct);

        result.NotFound.ShouldBe(1);
        _importer.Imports.ShouldBeEmpty();
        _read.Refreshed.ShouldBe([99]);
    }

    [Fact]
    public async Task Run_FailedGame_IsNotMarkedRefreshedSoItIsRetriedFirstTomorrow()
    {
        _read.Candidates.AddRange([1, 2]);
        _rawg.SearchResults.Add(NewRawg(2, "Two"));
        _rawg.FailingIds.Add(1);

        var result = await CreateService().RunAsync(Ct);

        result.Failed.ShouldBe(1);
        result.Refreshed.ShouldBe(1);
        _read.Refreshed.ShouldBe([2]);
    }

    [Fact]
    public async Task Run_ConsecutiveFailures_AbortsTheRun()
    {
        _options.MaxConsecutiveFailures = 3;
        _read.Candidates.AddRange([1, 2, 3, 4, 5]);
        _rawg.Fail = true;

        var result = await CreateService().RunAsync(Ct);

        result.Aborted.ShouldBeTrue();
        result.Failed.ShouldBe(3);
    }

    [Fact]
    public async Task Run_PurgesProcessedEventsOlderThanRetention()
    {
        await CreateService().RunAsync(Ct);

        _stats.PurgeCutoffs.ShouldBe([Now.UtcDateTime.AddDays(-14)]);
    }

    [Fact]
    public async Task Run_RawgDisabled_OnlyPurges()
    {
        _rawg.IsEnabled = false;
        _read.Candidates.Add(1);

        var result = await CreateService().RunAsync(Ct);

        result.Candidates.ShouldBe(0);
        _stats.PurgeCutoffs.Count.ShouldBe(1);
        _read.CandidateRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Run_WaitsRequestIntervalBetweenRawgCalls()
    {
        _options.RequestInterval = TimeSpan.FromSeconds(1);
        _read.Candidates.AddRange([1, 2]);
        _rawg.SearchResults.AddRange([NewRawg(1, "One"), NewRawg(2, "Two")]);

        var run = CreateService().RunAsync(Ct);
        await Task.Yield();
        _importer.Imports.Count.ShouldBe(1);

        _time.Advance(TimeSpan.FromSeconds(1));
        await run;
        _importer.Imports.Count.ShouldBe(2);
    }

    private sealed class FakeStatsStore : IGameStatsStore
    {
        public List<DateTime> PurgeCutoffs { get; } = [];

        public Task<StatsUpdateOutcome> ApplyAsync(string messageId, Guid gameId, StatsDelta delta, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> PurgeProcessedBeforeAsync(DateTime cutoff, CancellationToken cancellationToken)
        {
            PurgeCutoffs.Add(cutoff);
            return Task.FromResult(0);
        }
    }
}

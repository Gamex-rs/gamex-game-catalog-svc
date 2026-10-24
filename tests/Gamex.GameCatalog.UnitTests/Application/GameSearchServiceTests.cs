using Gamex.GameCatalog.Application.Games;
using Gamex.GameCatalog.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using static Gamex.GameCatalog.UnitTests.Fakes.TestGames;

namespace Gamex.GameCatalog.UnitTests.Application;

public sealed class GameSearchServiceTests
{
    private readonly FakeGameQueries _queries = new();
    private readonly FakeRawgClient _rawg = new();
    private readonly RecordingImporter _importer = new();

    private GameSearchService CreateService() =>
        new(_queries, _rawg, _importer, NullLogger<GameSearchService>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_EnoughLocalResults_DoesNotCallRawg()
    {
        _queries.SearchResponses.Add([Summary("A"), Summary("B"), Summary("C")]);

        var results = await CreateService().SearchAsync("ab", Ct);

        results.Count.ShouldBe(3);
        _rawg.Searches.ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_FewLocalResults_ImportsFromRawgAndSearchesAgain()
    {
        _queries.SearchResponses.Add([Summary("A")]);
        _queries.SearchResponses.Add([Summary("A"), Summary("B")]);
        _rawg.SearchResults.Add(NewRawg(1, "B"));

        var results = await CreateService().SearchAsync("ab", Ct);

        _rawg.Searches.ShouldBe(["ab"]);
        _importer.Imports.ShouldHaveSingleItem().ShouldHaveSingleItem().Name.ShouldBe("B");
        results.Select(r => r.Title).ShouldBe(["A", "B"]);
    }

    [Fact]
    public async Task Search_RawgFindsNothing_ReturnsLocalWithoutImport()
    {
        _queries.SearchResponses.Add([Summary("A")]);

        var results = await CreateService().SearchAsync("ab", Ct);

        results.Select(r => r.Title).ShouldBe(["A"]);
        _importer.Imports.ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_RawgUnavailable_ReturnsLocalResults()
    {
        _queries.SearchResponses.Add([Summary("A")]);
        _rawg.Fail = true;

        var results = await CreateService().SearchAsync("ab", Ct);

        results.Select(r => r.Title).ShouldBe(["A"]);
    }

    [Fact]
    public async Task Search_RawgDisabled_ReturnsLocalResults()
    {
        _queries.SearchResponses.Add([]);
        _rawg.IsEnabled = false;

        var results = await CreateService().SearchAsync("ab", Ct);

        results.ShouldBeEmpty();
        _rawg.Searches.ShouldBeEmpty();
    }
}

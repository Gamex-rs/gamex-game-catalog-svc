using Gamex.GameCatalog.Application.Common;
using Gamex.GameCatalog.Application.Games;
using Gamex.GameCatalog.Domain.Games;
using Gamex.GameCatalog.UnitTests.Fakes;
using Microsoft.Extensions.Time.Testing;
using static Gamex.GameCatalog.UnitTests.Fakes.TestGames;

namespace Gamex.GameCatalog.UnitTests.Application;

public sealed class GameAdminServiceTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);

    private readonly FakeCatalogRepository _repository = new();
    private readonly RecordingEventPublisher _events = new();
    private readonly FakeRawgClient _rawg = new();
    private readonly RecordingImporter _importer = new();
    private readonly DetailQueries _queries = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(T0.AddHours(1)));

    private readonly Platform _pc = Platform.Create("PC", "pc", T0);
    private readonly Platform _ps5 = Platform.Create("PlayStation 5", "playstation5", T0);
    private readonly Genre _action = Genre.Create("Action", "action");
    private readonly Game _game;

    public GameAdminServiceTests()
    {
        _repository.Platforms.AddRange([_pc, _ps5]);
        _repository.Genres.Add(_action);
        _game = Game.Create(new GameMetadata(1, "hades", "Hades", "Old", null, new DateOnly(2020, 9, 17), 93), [_pc], [_action], T0);
        _repository.Games.Add(_game);
    }

    private GameAdminService CreateService() =>
        new(_rawg, _importer, _queries, _repository, _events, _time);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private UpdateGameCommand Command(
        string? title = "Hades",
        string? description = "Old",
        string? cover = null,
        short? metacritic = 93,
        Guid[]? platformIds = null,
        Guid[]? genreIds = null) =>
        new(title, description, cover, new DateOnly(2020, 9, 17), metacritic, platformIds ?? [_pc.Id], genreIds ?? [_action.Id]);

    [Fact]
    public async Task Update_ReplicatedChange_SavesAndPublishes()
    {
        var result = await CreateService().UpdateAsync(_game.Id, Command(title: "Hades II", platformIds: [_pc.Id, _ps5.Id]), Ct);

        result.Status.ShouldBe(OperationStatus.Ok);
        _game.Title.ShouldBe("Hades II");
        _game.Platforms.Count.ShouldBe(2);
        _game.RawgId.ShouldBe(1);
        _repository.SaveCount.ShouldBe(1);
        _events.Log.ShouldBe(["game:hades"]);
    }

    [Fact]
    public async Task Update_DescriptionOnly_SavesWithoutPublishing()
    {
        await CreateService().UpdateAsync(_game.Id, Command(description: "  New  "), Ct);

        _game.Description.ShouldBe("New");
        _repository.SaveCount.ShouldBe(1);
        _events.Log.ShouldBeEmpty();
    }

    [Fact]
    public async Task Update_NoChange_DoesNotSave()
    {
        var result = await CreateService().UpdateAsync(_game.Id, Command(), Ct);

        result.Status.ShouldBe(OperationStatus.Ok);
        _repository.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Update_UnknownGame_ReturnsNotFound()
    {
        var result = await CreateService().UpdateAsync(Guid.NewGuid(), Command(), Ct);

        result.Status.ShouldBe(OperationStatus.NotFound);
    }

    [Fact]
    public async Task Update_UnknownPlatformAndGenre_ReturnsInvalid()
    {
        var result = await CreateService().UpdateAsync(_game.Id, Command(platformIds: [Guid.NewGuid()], genreIds: [Guid.NewGuid()]), Ct);

        result.Status.ShouldBe(OperationStatus.Invalid);
        result.Errors!.Keys.ShouldBe(["platformIds", "genreIds"], ignoreOrder: true);
        _repository.SaveCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(null, (short)90, null, "title")]
    [InlineData("  ", (short)90, null, "title")]
    [InlineData("Hades", (short)101, null, "metacritic")]
    [InlineData("Hades", (short)-1, null, "metacritic")]
    [InlineData("Hades", (short)90, "not a url", "coverImageUrl")]
    [InlineData("Hades", (short)90, "http://insecure.example/c.jpg", "coverImageUrl")]
    public async Task Update_InvalidFields_ReturnsInvalid(string? title, short metacritic, string? cover, string expectedKey)
    {
        var result = await CreateService().UpdateAsync(_game.Id, Command(title: title, metacritic: metacritic, cover: cover), Ct);

        result.Status.ShouldBe(OperationStatus.Invalid);
        result.Errors!.Keys.ShouldContain(expectedKey);
    }

    [Fact]
    public async Task Update_TitleTooLong_ReturnsInvalid()
    {
        var result = await CreateService().UpdateAsync(_game.Id, Command(title: new string('x', 256)), Ct);

        result.Errors!.Keys.ShouldBe(["title"]);
    }

    [Fact]
    public async Task Sync_ImportsRawgDetail()
    {
        _rawg.SearchResults.Add(NewRawg(7, "Celeste", description: "Climb."));

        var result = await CreateService().SyncFromRawgAsync(" slug-7 ", Ct);

        result.Status.ShouldBe(OperationStatus.Ok);
        _importer.Imports.ShouldHaveSingleItem().ShouldHaveSingleItem().Description.ShouldBe("Climb.");
    }

    [Fact]
    public async Task Sync_UnknownSlug_ReturnsNotFound()
    {
        var result = await CreateService().SyncFromRawgAsync("nope", Ct);

        result.Status.ShouldBe(OperationStatus.NotFound);
        _importer.Imports.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sync_MissingSlug_ReturnsInvalid()
    {
        (await CreateService().SyncFromRawgAsync("  ", Ct)).Status.ShouldBe(OperationStatus.Invalid);
    }

    [Fact]
    public async Task Sync_RawgFails_ReturnsDependencyFailed()
    {
        _rawg.Fail = true;

        (await CreateService().SyncFromRawgAsync("hades", Ct)).Status.ShouldBe(OperationStatus.DependencyFailed);
    }

    [Fact]
    public async Task Sync_RawgDisabled_ReturnsDependencyDisabled()
    {
        _rawg.IsEnabled = false;

        (await CreateService().SyncFromRawgAsync("hades", Ct)).Status.ShouldBe(OperationStatus.DependencyDisabled);
    }

    /// <summary>Returns a detail for any id; the admin service only passes it through.</summary>
    private sealed class DetailQueries : IGameQueries
    {
        public Task<GameDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<GameDetailDto?>(new GameDetailDto(id, "x", null, null, null, null, null, [], [], new GameStatsDto(0, 0, 0, 0, 0)));

        public Task<PagedResult<GameSummaryDto>> ListAsync(GameListQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<GameSummaryDto>> SearchAsync(string term, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<PlatformDto>> ListPlatformsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<GenreDto>> ListGenresAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

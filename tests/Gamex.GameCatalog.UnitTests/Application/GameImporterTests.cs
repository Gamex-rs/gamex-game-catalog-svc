using Gamex.GameCatalog.Application.Games;
using Gamex.GameCatalog.Domain.Games;
using Gamex.GameCatalog.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Gamex.GameCatalog.UnitTests.Fakes.TestGames;

namespace Gamex.GameCatalog.UnitTests.Application;

public sealed class GameImporterTests
{
    private readonly FakeCatalogRepository _repository = new();
    private readonly RecordingEventPublisher _events = new();
    private readonly RecordingCoverQueue _covers = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero));

    private GameImporter CreateImporter() =>
        new(_repository, _events, _covers, _time, NullLogger<GameImporter>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Import_NewGames_CreatesGamesPlatformsAndGenres()
    {
        var ids = await CreateImporter().ImportAsync(
            [NewRawg(1, "Elden Ring", ["playstation5", "pc"], ["action", "rpg"]), NewRawg(2, "Hades", ["pc"])],
            Ct);

        ids.Count.ShouldBe(2);
        _repository.Games.Select(g => g.Title).ShouldBe(["Elden Ring", "Hades"]);
        _repository.Platforms.Select(p => p.Slug).ShouldBe(["playstation5", "pc"], ignoreOrder: true);
        _repository.Genres.Select(g => g.Slug).ShouldBe(["action", "rpg"], ignoreOrder: true);
        _repository.Games[1].Platforms.ShouldHaveSingleItem().ShouldBeSameAs(_repository.Games[0].Platforms.Single(p => p.Slug == "pc"));
        _repository.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Import_PublishesPlatformEventsBeforeGameEvents()
    {
        await CreateImporter().ImportAsync([NewRawg(1, "Elden Ring", ["playstation5", "pc"])], Ct);

        _events.Log.ShouldBe(["platform:playstation5", "platform:pc", "game:slug-1"], ignoreOrder: false);
    }

    [Fact]
    public async Task Import_DuplicateRawgIdsInInput_ImportsOnce()
    {
        var ids = await CreateImporter().ImportAsync([NewRawg(1, "Elden Ring"), NewRawg(1, "Elden Ring")], Ct);

        ids.Count.ShouldBe(1);
        _repository.Games.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Import_ExistingUnchangedGame_PublishesNothing()
    {
        var importer = CreateImporter();
        await importer.ImportAsync([NewRawg(1, "Elden Ring")], Ct);
        _events.Log.Clear();

        var ids = await importer.ImportAsync([NewRawg(1, "Elden Ring")], Ct);

        ids.ShouldBe([_repository.Games.Single().Id]);
        _repository.Games.Count.ShouldBe(1);
        _events.Log.ShouldBeEmpty();
    }

    [Fact]
    public async Task Import_ExistingGameWithNewTitle_UpdatesAndPublishes()
    {
        var importer = CreateImporter();
        await importer.ImportAsync([NewRawg(1, "Elden Ring")], Ct);
        _events.Log.Clear();

        await importer.ImportAsync([NewRawg(1, "ELDEN RING")], Ct);

        _repository.Games.Single().Title.ShouldBe("ELDEN RING");
        _events.Log.ShouldBe(["game:slug-1"]);
    }

    [Fact]
    public async Task Import_SearchResultWithoutDescription_KeepsExistingDescription()
    {
        var importer = CreateImporter();
        await importer.ImportAsync([NewRawg(1, "Elden Ring", description: "Rise, Tarnished.")], Ct);

        await importer.ImportAsync([NewRawg(1, "Elden Ring", description: null)], Ct);

        _repository.Games.Single().Description.ShouldBe("Rise, Tarnished.");
    }

    [Fact]
    public async Task Import_RenamedPlatform_PublishesPlatformEvent()
    {
        var importer = CreateImporter();
        await importer.ImportAsync([NewRawg(1, "Elden Ring", ["pc"])], Ct);
        _events.Log.Clear();

        await importer.ImportAsync(
            [NewRawg(1, "Elden Ring") with { Platforms = [new("Windows PC", "pc")] }],
            Ct);

        _repository.Platforms.Single().Name.ShouldBe("Windows PC");
        _events.Log.ShouldBe(["platform:pc"]);
    }

    [Fact]
    public async Task Import_QueuesCoverOnlyWhenSourceChanges()
    {
        var importer = CreateImporter();
        await importer.ImportAsync([NewRawg(1, "Elden Ring", image: "https://rawg/a.jpg")], Ct);

        var game = _repository.Games.Single();
        _covers.Requests.ShouldBe([new(game.Id, 1, "https://rawg/a.jpg")]);

        game.SetCover("https://cdn/1.jpg", "https://rawg/a.jpg", _time.GetUtcNow().UtcDateTime);
        await importer.ImportAsync([NewRawg(1, "Elden Ring", image: "https://rawg/a.jpg")], Ct);
        _covers.Requests.Count.ShouldBe(1);

        await importer.ImportAsync([NewRawg(1, "Elden Ring", image: "https://rawg/b.jpg")], Ct);
        _covers.Requests.Count.ShouldBe(2);
        _covers.Requests[1].SourceUrl.ShouldBe("https://rawg/b.jpg");
    }

    [Fact]
    public async Task Import_KeepsStoredCoverUrl()
    {
        var importer = CreateImporter();
        await importer.ImportAsync([NewRawg(1, "Elden Ring", image: "https://rawg/a.jpg")], Ct);
        var game = _repository.Games.Single();
        game.SetCover("https://cdn/1.jpg", "https://rawg/a.jpg", _time.GetUtcNow().UtcDateTime);

        await importer.ImportAsync([NewRawg(1, "Elden Ring", image: "https://rawg/a.jpg")], Ct);

        game.CoverImageUrl.ShouldBe("https://cdn/1.jpg");
    }

    [Fact]
    public async Task Import_DuplicateKeyOnSave_ResetsAndRetriesOnce()
    {
        _repository.FailNextSaves = 1;

        await CreateImporter().ImportAsync([NewRawg(1, "Elden Ring")], Ct);

        _repository.ResetCount.ShouldBe(1);
        _repository.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Import_DuplicateKeyTwice_Throws()
    {
        _repository.FailNextSaves = 2;

        await Should.ThrowAsync<Gamex.GameCatalog.Application.Abstractions.DuplicateKeyException>(
            () => CreateImporter().ImportAsync([NewRawg(1, "Elden Ring")], Ct));
    }

    [Fact]
    public async Task Import_LongTitle_IsTruncated()
    {
        await CreateImporter().ImportAsync([NewRawg(1, new string('x', 300))], Ct);

        _repository.Games.Single().Title.Length.ShouldBe(255);
    }

    [Fact]
    public async Task Import_EmptyInput_DoesNothing()
    {
        var ids = await CreateImporter().ImportAsync([], Ct);

        ids.ShouldBeEmpty();
        _repository.SaveCount.ShouldBe(0);
    }

    [Fact]
    public void SetCover_SameUrls_ReturnsNone()
    {
        var game = Game.Create(new GameMetadata(1, "s", "T", null, null, null, null), [], [], DateTime.UtcNow);
        game.SetCover("https://cdn/1.jpg", "https://rawg/a.jpg", DateTime.UtcNow).ShouldBe(GameChange.Replicated);

        game.SetCover("https://cdn/1.jpg", "https://rawg/a.jpg", DateTime.UtcNow).ShouldBe(GameChange.None);
        game.NeedsCoverFrom("https://rawg/a.jpg").ShouldBeFalse();
        game.NeedsCoverFrom(null).ShouldBeFalse();
        game.NeedsCoverFrom("https://rawg/b.jpg").ShouldBeTrue();
    }
}

using Gamex.GameCatalog.Domain.Games;

namespace Gamex.GameCatalog.UnitTests.Domain;

public sealed class GameTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = T0.AddHours(1);

    private static readonly Platform Ps5 = Platform.Create("PlayStation 5", "playstation5", T0);
    private static readonly Platform Pc = Platform.Create("PC", "pc", T0);
    private static readonly Genre Action = Genre.Create("Action", "action");
    private static readonly Genre Rpg = Genre.Create("RPG", "role-playing-games-rpg");

    private static readonly GameMetadata EldenRing = new(
        RawgId: 326243,
        RawgSlug: "elden-ring",
        Title: "Elden Ring",
        Description: "Rise, Tarnished.",
        CoverImageUrl: "https://media.gamex.rs/game-covers/326243.jpg",
        ReleaseDate: new DateOnly(2022, 2, 25),
        Metacritic: 96);

    private static Game NewGame() => Game.Create(EldenRing, [Ps5, Pc], [Action], T0);

    [Fact]
    public void Create_SetsFieldsAndEmptyStats()
    {
        var game = NewGame();

        game.Title.ShouldBe("Elden Ring");
        game.Platforms.ShouldBe([Ps5, Pc], ignoreOrder: true);
        game.Genres.ShouldBe([Action]);
        game.CreatedAt.ShouldBe(T0);
        game.UpdatedAt.ShouldBe(T0);
        game.Stats.GameId.ShouldBe(game.Id);
        game.Stats.OwnedCount.ShouldBe(0u);
    }

    [Fact]
    public void Create_RejectsBlankTitle()
    {
        Should.Throw<ArgumentException>(() => Game.Create(EldenRing with { Title = " " }, [], [], T0));
    }

    [Fact]
    public void Update_WithSameData_ReturnsNoneAndKeepsVersion()
    {
        var game = NewGame();

        var change = game.Update(EldenRing, [Pc, Ps5], [Action], T1);

        change.ShouldBe(GameChange.None);
        game.UpdatedAt.ShouldBe(T0);
    }

    [Theory]
    [MemberData(nameof(ReplicatedFieldChanges))]
    public void Update_ReplicatedFieldChanged_ReturnsReplicated(GameMetadata metadata)
    {
        var game = NewGame();

        var change = game.Update(metadata, [Ps5, Pc], [Action], T1);

        change.ShouldBe(GameChange.Replicated);
        game.UpdatedAt.ShouldBe(T1);
    }

    public static TheoryData<GameMetadata> ReplicatedFieldChanges() =>
    [
        EldenRing with { Title = "ELDEN RING" },
        EldenRing with { CoverImageUrl = "https://media.gamex.rs/game-covers/other.jpg" },
        EldenRing with { ReleaseDate = new DateOnly(2022, 2, 24) },
        EldenRing with { Metacritic = 95 },
    ];

    [Fact]
    public void Update_PlatformRemoved_ReturnsReplicated()
    {
        var game = NewGame();

        var change = game.Update(EldenRing, [Ps5], [Action], T1);

        change.ShouldBe(GameChange.Replicated);
        game.Platforms.ShouldBe([Ps5]);
    }

    [Fact]
    public void Update_OnlyDescriptionAndGenresChanged_ReturnsInternalAndBumpsVersion()
    {
        var game = NewGame();

        var change = game.Update(EldenRing with { Description = "New text" }, [Ps5, Pc], [Action, Rpg], T1);

        change.ShouldBe(GameChange.Internal);
        game.Description.ShouldBe("New text");
        game.Genres.ShouldBe([Action, Rpg], ignoreOrder: true);
        game.UpdatedAt.ShouldBe(T1);
    }

    [Fact]
    public void Update_DuplicatePlatforms_AreIgnored()
    {
        var game = Game.Create(EldenRing, [Ps5, Ps5], [], T0);

        game.Platforms.Count.ShouldBe(1);
    }
}

namespace Gamex.GameCatalog.Domain.Games;

public sealed class Game
{
    private readonly List<Platform> _platforms = [];
    private readonly List<Genre> _genres = [];

    private Game()
    {
        Title = null!;
        Stats = null!;
    }

    public Guid Id { get; private set; }

    /// <summary>Null for manually added games.</summary>
    public int? RawgId { get; private set; }
    public string? RawgSlug { get; private set; }
    public string Title { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Cloudflare CDN URL of the cover stored in S3.</summary>
    public string? CoverImageUrl { get; private set; }
    public DateOnly? ReleaseDate { get; private set; }
    public short? Metacritic { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>Bumped on any change; version for <c>catalog.game.upserted</c> events.</summary>
    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyCollection<Platform> Platforms => _platforms;
    public IReadOnlyCollection<Genre> Genres => _genres;
    public GameStats Stats { get; private set; }

    public static Game Create(
        GameMetadata metadata,
        IEnumerable<Platform> platforms,
        IEnumerable<Genre> genres,
        DateTime now)
    {
        var game = new Game
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = now,
        };
        game.Stats = GameStats.CreateFor(game.Id, now);
        game.Apply(metadata, platforms, genres, now);
        return game;
    }

    public GameChange Update(
        GameMetadata metadata,
        IEnumerable<Platform> platforms,
        IEnumerable<Genre> genres,
        DateTime now) => Apply(metadata, platforms, genres, now);

    private GameChange Apply(
        GameMetadata metadata,
        IEnumerable<Platform> platforms,
        IEnumerable<Genre> genres,
        DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.Title);

        var newPlatforms = platforms.DistinctBy(p => p.Id).ToList();
        var newGenres = genres.DistinctBy(g => g.Id).ToList();

        var replicatedChanged =
            Title != metadata.Title
            || CoverImageUrl != metadata.CoverImageUrl
            || ReleaseDate != metadata.ReleaseDate
            || Metacritic != metadata.Metacritic
            || !SameIds(_platforms.Select(p => p.Id), newPlatforms.Select(p => p.Id));

        var internalChanged =
            RawgId != metadata.RawgId
            || RawgSlug != metadata.RawgSlug
            || Description != metadata.Description
            || !SameIds(_genres.Select(g => g.Id), newGenres.Select(g => g.Id));

        if (!replicatedChanged && !internalChanged)
        {
            return GameChange.None;
        }

        RawgId = metadata.RawgId;
        RawgSlug = metadata.RawgSlug;
        Title = metadata.Title;
        Description = metadata.Description;
        CoverImageUrl = metadata.CoverImageUrl;
        ReleaseDate = metadata.ReleaseDate;
        Metacritic = metadata.Metacritic;
        Replace(_platforms, newPlatforms);
        Replace(_genres, newGenres);
        UpdatedAt = now;

        return replicatedChanged ? GameChange.Replicated : GameChange.Internal;
    }

    private static bool SameIds(IEnumerable<Guid> current, IEnumerable<Guid> next) =>
        current.ToHashSet().SetEquals(next);

    // Mutate in place so EF Core tracks join-table inserts/deletes instead of a full replace.
    private static void Replace<T>(List<T> current, List<T> next)
    {
        current.RemoveAll(item => !next.Contains(item));
        current.AddRange(next.Where(item => !current.Contains(item)));
    }
}

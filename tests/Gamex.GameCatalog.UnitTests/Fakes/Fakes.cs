using System.Net;
using Gamex.GameCatalog.Application.Abstractions;
using Gamex.GameCatalog.Application.Games;
using Gamex.GameCatalog.Application.Rawg;
using Gamex.GameCatalog.Domain.Games;

namespace Gamex.GameCatalog.UnitTests.Fakes;

internal sealed class FakeCatalogRepository : ICatalogRepository
{
    public List<Platform> Platforms { get; } = [];
    public List<Genre> Genres { get; } = [];
    public List<Game> Games { get; } = [];
    public int SaveCount { get; private set; }
    public int ResetCount { get; private set; }

    /// <summary>Number of upcoming saves that fail with a duplicate key.</summary>
    public int FailNextSaves { get; set; }

    public Task<List<Platform>> GetPlatformsBySlugAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken) =>
        Task.FromResult(Platforms.Where(p => slugs.Contains(p.Slug)).ToList());

    public Task<List<Genre>> GetGenresBySlugAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken) =>
        Task.FromResult(Genres.Where(g => slugs.Contains(g.Slug)).ToList());

    public Task<List<Platform>> GetPlatformsByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult(Platforms.Where(p => ids.Contains(p.Id)).ToList());

    public Task<List<Genre>> GetGenresByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult(Genres.Where(g => ids.Contains(g.Id)).ToList());

    public Task<List<Game>> GetGamesByRawgIdAsync(IReadOnlyCollection<int> rawgIds, CancellationToken cancellationToken) =>
        Task.FromResult(Games.Where(g => g.RawgId is { } id && rawgIds.Contains(id)).ToList());

    public Task<Game?> GetGameAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Games.SingleOrDefault(g => g.Id == id));

    public void Add(Platform platform) => Platforms.Add(platform);

    public void Add(Genre genre) => Genres.Add(genre);

    public void Add(Game game) => Games.Add(game);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (FailNextSaves > 0)
        {
            FailNextSaves--;
            throw new DuplicateKeyException();
        }

        SaveCount++;
        return Task.CompletedTask;
    }

    public void Reset() => ResetCount++;
}

internal sealed class FakeCatalogReadRepository(FakeCatalogRepository source) : ICatalogReadRepository
{
    public List<(int Skip, int Take)> BatchRequests { get; } = [];

    public Task<List<Platform>> GetAllPlatformsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(source.Platforms.ToList());

    public Task<List<Platform>> GetPlatformsOfGamesAsync(IReadOnlyCollection<Guid> gameIds, CancellationToken cancellationToken) =>
        Task.FromResult(source.Games.Where(g => gameIds.Contains(g.Id)).SelectMany(g => g.Platforms).Distinct().ToList());

    public Task<List<Game>> GetGamesBatchAsync(int skip, int take, CancellationToken cancellationToken)
    {
        BatchRequests.Add((skip, take));
        return Task.FromResult(source.Games.OrderBy(g => g.Id).Skip(skip).Take(take).ToList());
    }

    public Task<List<Game>> GetGamesByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult(source.Games.Where(g => ids.Contains(g.Id)).ToList());

    public List<int> Candidates { get; } = [];
    public List<int> CandidateRequests { get; } = [];
    public List<int> Refreshed { get; } = [];

    public Task<List<int>> GetRefreshCandidatesAsync(int take, CancellationToken cancellationToken)
    {
        CandidateRequests.Add(take);
        return Task.FromResult(Candidates.Take(take).ToList());
    }

    public Task MarkRefreshedAsync(int rawgId, DateTime refreshedAt, CancellationToken cancellationToken)
    {
        Refreshed.Add(rawgId);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingEventPublisher : ICatalogEventPublisher
{
    public List<string> Log { get; } = [];

    public Task PublishPlatformsAsync(IReadOnlyCollection<Platform> platforms, CancellationToken cancellationToken)
    {
        Log.AddRange(platforms.Select(p => $"platform:{p.Slug}"));
        return Task.CompletedTask;
    }

    public Task PublishGamesAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken)
    {
        Log.AddRange(games.Select(g => $"game:{g.RawgSlug}"));
        return Task.CompletedTask;
    }
}

internal sealed class RecordingCoverQueue : ICoverImageQueue
{
    public List<CoverImageRequest> Requests { get; } = [];

    public bool TryEnqueue(CoverImageRequest request)
    {
        Requests.Add(request);
        return true;
    }
}

internal sealed class FakeRawgClient : IRawgClient
{
    public bool IsEnabled { get; set; } = true;
    public List<RawgGame> SearchResults { get; } = [];

    /// <summary>When set, every call throws <see cref="RawgUnavailableException"/>.</summary>
    public bool Fail { get; set; }

    /// <summary>Detail lookups for these RAWG ids throw <see cref="RawgUnavailableException"/>.</summary>
    public HashSet<int> FailingIds { get; } = [];
    public List<string> Searches { get; } = [];

    public Task<IReadOnlyList<RawgGame>> SearchAsync(string term, int pageSize, CancellationToken cancellationToken)
    {
        Searches.Add(term);
        return Fail
            ? throw new RawgUnavailableException("down")
            : Task.FromResult<IReadOnlyList<RawgGame>>(SearchResults);
    }

    public Task<RawgGame?> GetGameAsync(string idOrSlug, CancellationToken cancellationToken) =>
        Fail || (int.TryParse(idOrSlug, out var id) && FailingIds.Contains(id))
            ? throw new RawgUnavailableException("down")
            : Task.FromResult(SearchResults.FirstOrDefault(g => g.Slug == idOrSlug || g.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) == idOrSlug));
}

internal sealed class FakeGameQueries : IGameQueries
{
    private int _searchCalls;

    /// <summary>Results returned by each successive <see cref="SearchAsync"/> call.</summary>
    public List<IReadOnlyList<GameSummaryDto>> SearchResponses { get; } = [];

    public Task<IReadOnlyList<GameSummaryDto>> SearchAsync(string term, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(SearchResponses[Math.Min(_searchCalls++, SearchResponses.Count - 1)]);

    public Task<PagedResult<GameSummaryDto>> ListAsync(GameListQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<GameDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<PlatformDto>> ListPlatformsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<GenreDto>> ListGenresAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class RecordingImporter : IGameImporter
{
    public List<IReadOnlyList<RawgGame>> Imports { get; } = [];

    public Task<IReadOnlyList<Guid>> ImportAsync(IReadOnlyList<RawgGame> rawgGames, CancellationToken cancellationToken)
    {
        Imports.Add(rawgGames);
        return Task.FromResult<IReadOnlyList<Guid>>(rawgGames.Select(_ => Guid.NewGuid()).ToList());
    }
}

internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(respond(request));
    }

    public static StubHttpHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
}

internal static class TestGames
{
    public static RawgGame NewRawg(
        int id,
        string name,
        string[]? platforms = null,
        string[]? genres = null,
        string? description = null,
        string? image = null) => new(
        id,
        $"slug-{id}",
        name,
        description,
        new DateOnly(2022, 2, 25),
        90,
        image,
        (platforms ?? ["pc"]).Select(slug => new RawgRef(slug.ToUpperInvariant(), slug)).ToList(),
        (genres ?? ["action"]).Select(slug => new RawgRef(slug, slug)).ToList());

    public static GameSummaryDto Summary(string title) =>
        new(Guid.NewGuid(), title, null, null, null, [], new GameStatsDto(0, 0, 0, 0, 0));
}

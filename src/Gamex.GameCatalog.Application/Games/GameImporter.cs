using Gamex.GameCatalog.Application.Abstractions;
using Gamex.GameCatalog.Application.Rawg;
using Gamex.GameCatalog.Domain.Games;
using Microsoft.Extensions.Logging;

namespace Gamex.GameCatalog.Application.Games;

public interface IGameImporter
{
    /// <summary>Creates or updates games (matched by RAWG id) with their platforms and genres.</summary>
    /// <returns>Local game ids in the order of <paramref name="rawgGames"/> (duplicates removed).</returns>
    Task<IReadOnlyList<Guid>> ImportAsync(IReadOnlyList<RawgGame> rawgGames, CancellationToken cancellationToken);
}

internal sealed partial class GameImporter(
    ICatalogRepository repository,
    ICatalogEventPublisher events,
    ICoverImageQueue covers,
    TimeProvider time,
    ILogger<GameImporter> logger) : IGameImporter
{
    private const int MaxNameLength = 100;
    private const int MaxTitleLength = 255;
    private const int MaxSlugLength = 200;

    public async Task<IReadOnlyList<Guid>> ImportAsync(IReadOnlyList<RawgGame> rawgGames, CancellationToken cancellationToken)
    {
        var distinct = rawgGames.DistinctBy(g => g.Id).ToList();
        if (distinct.Count == 0)
        {
            return [];
        }

        try
        {
            return await ImportOnceAsync(distinct, cancellationToken);
        }
        catch (DuplicateKeyException ex)
        {
            // A concurrent import inserted the same game, platform or genre; reload and apply on top of it.
            LogConcurrentImport(logger, ex);
            repository.Reset();
            return await ImportOnceAsync(distinct, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<Guid>> ImportOnceAsync(List<RawgGame> rawgGames, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;

        var (platforms, changedPlatforms) = await ResolvePlatformsAsync(rawgGames, now, ct);
        var genres = await ResolveGenresAsync(rawgGames, ct);

        var existing = (await repository.GetGamesByRawgIdAsync(rawgGames.Select(g => g.Id).ToList(), ct))
            .ToDictionary(g => g.RawgId!.Value);

        var changedGames = new List<Game>();
        var coverRequests = new List<(Game Game, int RawgId, string SourceUrl)>();
        var result = new List<Guid>(rawgGames.Count);

        foreach (var rawg in rawgGames)
        {
            existing.TryGetValue(rawg.Id, out var game);

            var metadata = new GameMetadata(
                RawgId: rawg.Id,
                RawgSlug: Truncate(rawg.Slug, MaxSlugLength),
                Title: Truncate(rawg.Name, MaxTitleLength),
                // Search results carry no description; keep the one from an earlier detail sync.
                Description: rawg.Description ?? game?.Description,
                CoverImageUrl: game?.CoverImageUrl,
                ReleaseDate: rawg.Released,
                Metacritic: rawg.Metacritic);

            var gamePlatforms = rawg.Platforms.Select(p => platforms[p.Slug]);
            var gameGenres = rawg.Genres.Select(g => genres[g.Slug]);

            if (game is null)
            {
                game = Game.Create(metadata, gamePlatforms, gameGenres, now);
                repository.Add(game);
                changedGames.Add(game);
            }
            else if (game.Update(metadata, gamePlatforms, gameGenres, now) == GameChange.Replicated)
            {
                changedGames.Add(game);
            }

            if (game.NeedsCoverFrom(rawg.BackgroundImage))
            {
                coverRequests.Add((game, rawg.Id, rawg.BackgroundImage!));
            }

            result.Add(game.Id);
        }

        await repository.SaveChangesAsync(ct);

        // Platforms first: consumers need them before games that reference them.
        if (changedPlatforms.Count > 0)
        {
            await events.PublishPlatformsAsync(changedPlatforms, ct);
        }

        if (changedGames.Count > 0)
        {
            await events.PublishGamesAsync(changedGames, ct);
        }

        foreach (var (game, rawgId, sourceUrl) in coverRequests)
        {
            if (!covers.TryEnqueue(new CoverImageRequest(game.Id, rawgId, sourceUrl)))
            {
                LogCoverQueueFull(logger, game.Id);
            }
        }

        LogImported(logger, rawgGames.Count, changedGames.Count);
        return result;
    }

    private async Task<(Dictionary<string, Platform> BySlug, List<Platform> Changed)> ResolvePlatformsAsync(
        List<RawgGame> rawgGames, DateTime now, CancellationToken ct)
    {
        var wanted = rawgGames.SelectMany(g => g.Platforms).DistinctBy(p => p.Slug).ToList();
        var bySlug = (await repository.GetPlatformsBySlugAsync(wanted.Select(p => p.Slug).ToList(), ct))
            .ToDictionary(p => p.Slug);
        var changed = new List<Platform>();

        foreach (var rawg in wanted)
        {
            var name = Truncate(rawg.Name, MaxNameLength);
            if (bySlug.TryGetValue(rawg.Slug, out var platform))
            {
                if (platform.Rename(name, now))
                {
                    changed.Add(platform);
                }
            }
            else
            {
                platform = Platform.Create(name, Truncate(rawg.Slug, MaxNameLength), now);
                repository.Add(platform);
                bySlug[rawg.Slug] = platform;
                changed.Add(platform);
            }
        }

        return (bySlug, changed);
    }

    private async Task<Dictionary<string, Genre>> ResolveGenresAsync(List<RawgGame> rawgGames, CancellationToken ct)
    {
        var wanted = rawgGames.SelectMany(g => g.Genres).DistinctBy(g => g.Slug).ToList();
        var bySlug = (await repository.GetGenresBySlugAsync(wanted.Select(g => g.Slug).ToList(), ct))
            .ToDictionary(g => g.Slug);

        foreach (var rawg in wanted.Where(g => !bySlug.ContainsKey(g.Slug)))
        {
            var genre = Genre.Create(Truncate(rawg.Name, MaxNameLength), Truncate(rawg.Slug, MaxNameLength));
            repository.Add(genre);
            bySlug[rawg.Slug] = genre;
        }

        return bySlug;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    [LoggerMessage(Level = LogLevel.Information, Message = "Imported {Count} RAWG games, {Changed} new or changed")]
    private static partial void LogImported(ILogger logger, int count, int changed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Concurrent import detected, retrying")]
    private static partial void LogConcurrentImport(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cover queue full, skipped cover for game {GameId}")]
    private static partial void LogCoverQueueFull(ILogger logger, Guid gameId);
}

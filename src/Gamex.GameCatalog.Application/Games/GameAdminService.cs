using Gamex.GameCatalog.Application.Abstractions;
using Gamex.GameCatalog.Application.Common;
using Gamex.GameCatalog.Application.Rawg;
using Gamex.GameCatalog.Domain.Games;

namespace Gamex.GameCatalog.Application.Games;

/// <summary>Manual edit of a game. Platforms and genres are replaced by the given sets.</summary>
public sealed record UpdateGameCommand(
    string? Title,
    string? Description,
    string? CoverImageUrl,
    DateOnly? ReleaseDate,
    short? Metacritic,
    IReadOnlyList<Guid>? PlatformIds,
    IReadOnlyList<Guid>? GenreIds);

public interface IGameAdminService
{
    /// <summary>Fetches full RAWG metadata (including description) and imports it.</summary>
    Task<OperationResult<GameDetailDto>> SyncFromRawgAsync(string? rawgSlugOrId, CancellationToken cancellationToken);

    Task<OperationResult<GameDetailDto>> UpdateAsync(Guid id, UpdateGameCommand command, CancellationToken cancellationToken);
}

internal sealed class GameAdminService(
    IRawgClient rawg,
    IGameImporter importer,
    IGameQueries queries,
    ICatalogRepository repository,
    ICatalogEventPublisher events,
    TimeProvider time) : IGameAdminService
{
    private const int MaxTitleLength = 255;
    private const int MaxUrlLength = 500;

    public async Task<OperationResult<GameDetailDto>> SyncFromRawgAsync(string? rawgSlugOrId, CancellationToken cancellationToken)
    {
        var key = rawgSlugOrId?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return OperationResult<GameDetailDto>.Invalid(new Dictionary<string, string[]> { ["rawgSlug"] = ["Required."] });
        }

        if (!rawg.IsEnabled)
        {
            return OperationResult<GameDetailDto>.DependencyDisabled("RAWG_API_KEY is not configured.");
        }

        RawgGame? found;
        try
        {
            found = await rawg.GetGameAsync(key, cancellationToken);
        }
        catch (RawgUnavailableException)
        {
            return OperationResult<GameDetailDto>.DependencyFailed("RAWG did not respond successfully. Try again later.");
        }

        if (found is null)
        {
            return OperationResult<GameDetailDto>.NotFound($"RAWG has no game '{key}'.");
        }

        var ids = await importer.ImportAsync([found], cancellationToken);
        var detail = await queries.GetAsync(ids[0], cancellationToken);
        return OperationResult<GameDetailDto>.Ok(detail!);
    }

    public async Task<OperationResult<GameDetailDto>> UpdateAsync(Guid id, UpdateGameCommand command, CancellationToken cancellationToken)
    {
        var errors = Validate(command);
        if (errors.Count > 0)
        {
            return OperationResult<GameDetailDto>.Invalid(errors);
        }

        var game = await repository.GetGameAsync(id, cancellationToken);
        if (game is null)
        {
            return OperationResult<GameDetailDto>.NotFound($"No game with id {id} exists in the catalog");
        }

        var platformIds = command.PlatformIds?.Distinct().ToList() ?? [];
        var genreIds = command.GenreIds?.Distinct().ToList() ?? [];
        var platforms = await repository.GetPlatformsByIdAsync(platformIds, cancellationToken);
        var genres = await repository.GetGenresByIdAsync(genreIds, cancellationToken);

        var unknown = new Dictionary<string, string[]>();
        var missingPlatforms = platformIds.Except(platforms.Select(p => p.Id)).ToList();
        if (missingPlatforms.Count > 0)
        {
            unknown["platformIds"] = [$"Unknown platform ids: {string.Join(", ", missingPlatforms)}"];
        }

        var missingGenres = genreIds.Except(genres.Select(g => g.Id)).ToList();
        if (missingGenres.Count > 0)
        {
            unknown["genreIds"] = [$"Unknown genre ids: {string.Join(", ", missingGenres)}"];
        }

        if (unknown.Count > 0)
        {
            return OperationResult<GameDetailDto>.Invalid(unknown);
        }

        var metadata = new GameMetadata(
            game.RawgId,
            game.RawgSlug,
            command.Title!.Trim(),
            string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
            string.IsNullOrWhiteSpace(command.CoverImageUrl) ? null : command.CoverImageUrl.Trim(),
            command.ReleaseDate,
            command.Metacritic);

        var change = game.Update(metadata, platforms, genres, time.GetUtcNow().UtcDateTime);
        if (change != GameChange.None)
        {
            await repository.SaveChangesAsync(cancellationToken);
        }

        if (change == GameChange.Replicated)
        {
            await events.PublishGamesAsync([game], cancellationToken);
        }

        return OperationResult<GameDetailDto>.Ok((await queries.GetAsync(id, cancellationToken))!);
    }

    private static Dictionary<string, string[]> Validate(UpdateGameCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(command.Title))
        {
            errors["title"] = ["Required."];
        }
        else if (command.Title.Trim().Length > MaxTitleLength)
        {
            errors["title"] = [$"At most {MaxTitleLength} characters."];
        }

        if (command.Metacritic is < 0 or > 100)
        {
            errors["metacritic"] = ["Must be between 0 and 100."];
        }

        if (!string.IsNullOrWhiteSpace(command.CoverImageUrl)
            && (command.CoverImageUrl.Length > MaxUrlLength
                || !Uri.TryCreate(command.CoverImageUrl.Trim(), UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps))
        {
            errors["coverImageUrl"] = [$"Must be an absolute https URL of at most {MaxUrlLength} characters."];
        }

        return errors;
    }
}

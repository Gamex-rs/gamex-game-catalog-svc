namespace Gamex.GameCatalog.Domain.Games;

/// <summary>Game fields that come from RAWG or an admin edit.</summary>
public sealed record GameMetadata(
    int? RawgId,
    string? RawgSlug,
    string Title,
    string? Description,
    string? CoverImageUrl,
    DateOnly? ReleaseDate,
    short? Metacritic);

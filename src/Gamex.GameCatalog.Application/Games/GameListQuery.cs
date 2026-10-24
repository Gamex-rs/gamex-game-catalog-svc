namespace Gamex.GameCatalog.Application.Games;

public enum GameSortBy
{
    Title,
    ReleaseDate,
    Metacritic,
    OwnedCount,
}

public enum SortDirection
{
    Asc,
    Desc,
}

public sealed record GameListQuery(
    string? Q,
    IReadOnlyList<Guid> PlatformIds,
    Guid? GenreId,
    GameSortBy SortBy,
    SortDirection SortDirection,
    int Page,
    int PageSize)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    /// <summary>Parses and validates raw query-string values.</summary>
    /// <returns>The query, or validation errors keyed by parameter name.</returns>
    public static (GameListQuery? Query, Dictionary<string, string[]> Errors) Create(
        string? q,
        IReadOnlyList<Guid>? platformIds,
        Guid? genreId,
        string? sortBy,
        string? sortDir,
        int? page,
        int? pageSize)
    {
        var errors = new Dictionary<string, string[]>();

        var sort = GameSortBy.Title;
        if (sortBy is not null && !Enum.TryParse(sortBy, ignoreCase: true, out sort))
        {
            errors["sortBy"] = ["Allowed values: title, releaseDate, metacritic, ownedCount."];
        }

        var direction = SortDirection.Asc;
        if (sortDir is not null && !Enum.TryParse(sortDir, ignoreCase: true, out direction))
        {
            errors["sortDir"] = ["Allowed values: asc, desc."];
        }

        if (page is < 1)
        {
            errors["page"] = ["Must be 1 or greater."];
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"Must be between 1 and {MaxPageSize}."];
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var query = new GameListQuery(
            string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            platformIds?.Distinct().ToList() ?? [],
            genreId,
            sort,
            direction,
            page ?? 1,
            pageSize ?? DefaultPageSize);

        return (query, errors);
    }
}

using Gamex.GameCatalog.Application.Games;

namespace Gamex.GameCatalog.Api.Endpoints;

internal static class GameEndpoints
{
    private const int SearchMinLength = 2;

    public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        var games = app.MapGroup("/api/v1/games").WithTags("Games");

        games.MapGet("/", ListGames);
        games.MapGet("/search", SearchGames);
        games.MapGet("/{id:guid}", GetGame);

        var lookups = app.MapGroup("/api/v1").WithTags("Lookups");
        lookups.MapGet("/platforms", (IGameQueries queries, CancellationToken ct) => queries.ListPlatformsAsync(ct));
        lookups.MapGet("/genres", (IGameQueries queries, CancellationToken ct) => queries.ListGenresAsync(ct));

        return app;
    }

    private static async Task<IResult> ListGames(
        IGameQueries queries,
        CancellationToken ct,
        string? q = null,
        Guid[]? platformIds = null,
        Guid? genreId = null,
        string? sortBy = null,
        string? sortDir = null,
        int? page = null,
        int? pageSize = null)
    {
        var (query, errors) = GameListQuery.Create(q, platformIds, genreId, sortBy, sortDir, page, pageSize);
        if (query is null)
        {
            return Problems.Validation(errors);
        }

        return Results.Ok(await queries.ListAsync(query, ct));
    }

    private static async Task<IResult> SearchGames(IGameSearchService search, CancellationToken ct, string? q = null)
    {
        var term = q?.Trim();
        if (term is null || term.Length < SearchMinLength)
        {
            return Problems.Validation(new Dictionary<string, string[]>
            {
                ["q"] = [$"Must be at least {SearchMinLength} characters."],
            });
        }

        return Results.Ok(await search.SearchAsync(term, ct));
    }

    private static async Task<IResult> GetGame(Guid id, IGameQueries queries, CancellationToken ct)
    {
        var game = await queries.GetAsync(id, ct);
        return game is null
            ? Problems.NotFound("Game not found", $"No game with id {id} exists in the catalog")
            : Results.Ok(game);
    }
}

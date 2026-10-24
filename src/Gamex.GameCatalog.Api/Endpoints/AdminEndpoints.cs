using Gamex.GameCatalog.Api.Auth;
using Gamex.GameCatalog.Application.Abstractions;
using Gamex.GameCatalog.Application.Games;

namespace Gamex.GameCatalog.Api.Endpoints;

internal static class AdminEndpoints
{
    private const int MaxReplayGameIds = 1000;

    public sealed record SyncGameRequest(string? RawgSlug);

    public sealed record UpdateGameRequest(
        string? Title,
        string? Description,
        string? CoverImageUrl,
        DateOnly? ReleaseDate,
        short? Metacritic,
        Guid[]? PlatformIds,
        Guid[]? GenreIds);

    public sealed record ReplayAccepted(Guid JobId);

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var games = app.MapGroup("/api/v1/games")
            .WithTags("Admin")
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);

        games.MapPost("/sync", SyncGame);
        games.MapPut("/{id:guid}", UpdateGame);

        app.MapPost("/api/v1/admin/catalog-events/replay", Replay)
            .WithTags("Admin")
            .RequireAuthorization(AuthenticationSetup.AdminPolicy);

        return app;
    }

    private static async Task<IResult> SyncGame(SyncGameRequest request, IGameAdminService admin, CancellationToken ct) =>
        Problems.From(await admin.SyncFromRawgAsync(request.RawgSlug, ct), "RAWG game not found", Results.Ok);

    private static async Task<IResult> UpdateGame(Guid id, UpdateGameRequest request, IGameAdminService admin, CancellationToken ct)
    {
        var command = new UpdateGameCommand(
            request.Title,
            request.Description,
            request.CoverImageUrl,
            request.ReleaseDate,
            request.Metacritic,
            request.PlatformIds,
            request.GenreIds);

        return Problems.From(await admin.UpdateAsync(id, command, ct), "Game not found", Results.Ok);
    }

    private static IResult Replay(IReplayJobQueue queue, string? gameIds = null)
    {
        List<Guid>? ids = null;
        if (!string.IsNullOrWhiteSpace(gameIds))
        {
            ids = [];
            foreach (var part in gameIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Guid.TryParse(part, out var id))
                {
                    return Problems.Validation(new Dictionary<string, string[]> { ["gameIds"] = [$"'{part}' is not a valid id."] });
                }

                ids.Add(id);
            }

            if (ids.Count > MaxReplayGameIds)
            {
                return Problems.Validation(new Dictionary<string, string[]> { ["gameIds"] = [$"At most {MaxReplayGameIds} ids."] });
            }
        }

        var job = new ReplayRequest(Guid.CreateVersion7(), ids);
        if (!queue.TryEnqueue(job))
        {
            return Results.Problem(
                type: "https://gamex.rs/errors/busy",
                title: "Too many replays queued",
                detail: "Wait for running replays to finish and try again.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        return Results.Accepted(value: new ReplayAccepted(job.JobId));
    }
}

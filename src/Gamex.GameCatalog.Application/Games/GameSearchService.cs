using Gamex.GameCatalog.Application.Rawg;
using Microsoft.Extensions.Logging;

namespace Gamex.GameCatalog.Application.Games;

public interface IGameSearchService
{
    Task<IReadOnlyList<GameSummaryDto>> SearchAsync(string term, CancellationToken cancellationToken);
}

/// <summary>Local search with RAWG fallback: when fewer than 3 local results exist, RAWG results are imported first.</summary>
internal sealed partial class GameSearchService(
    IGameQueries queries,
    IRawgClient rawg,
    IGameImporter importer,
    ILogger<GameSearchService> logger) : IGameSearchService
{
    public const int Limit = 10;
    public const int MinLocalResults = 3;

    public async Task<IReadOnlyList<GameSummaryDto>> SearchAsync(string term, CancellationToken cancellationToken)
    {
        var local = await queries.SearchAsync(term, Limit, cancellationToken);
        if (local.Count >= MinLocalResults || !rawg.IsEnabled)
        {
            return local;
        }

        try
        {
            var found = await rawg.SearchAsync(term, Limit, cancellationToken);
            if (found.Count == 0)
            {
                return local;
            }

            await importer.ImportAsync(found, cancellationToken);
        }
        catch (RawgUnavailableException ex)
        {
            // Degrade to local results rather than failing autocomplete.
            LogRawgUnavailable(logger, ex, term);
            return local;
        }

        return await queries.SearchAsync(term, Limit, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAWG search failed for '{Term}', returning local results")]
    private static partial void LogRawgUnavailable(ILogger logger, Exception exception, string term);
}

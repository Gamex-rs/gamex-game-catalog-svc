using Gamex.GameCatalog.Application.Abstractions;
using Gamex.GameCatalog.Application.Games;
using Gamex.GameCatalog.Application.Rawg;
using Gamex.GameCatalog.Application.Stats;
using Microsoft.Extensions.Logging;

namespace Gamex.GameCatalog.Application.Refresh;

public sealed class NightlyRefreshOptions
{
    /// <summary>Upper bound of RAWG detail requests per night (RAWG quotas are monthly).</summary>
    public int MaxGames { get; set; } = 500;

    /// <summary>Pause between RAWG requests.</summary>
    public TimeSpan RequestInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Stop the run after this many consecutive RAWG failures; RAWG is probably down.</summary>
    public int MaxConsecutiveFailures { get; set; } = 5;

    /// <summary>Processed library-event ids older than this are deleted (SQS keeps messages at most 14 days).</summary>
    public TimeSpan ProcessedEventRetention { get; set; } = TimeSpan.FromDays(14);
}

public sealed record NightlyRefreshResult(int Candidates, int Refreshed, int NotFound, int Failed, bool Aborted, int PurgedEvents);

public interface INightlyRefreshService
{
    Task<NightlyRefreshResult> RunAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Refreshes RAWG metadata (incl. description and cover) for games that matter to users,
/// and purges old idempotency records of the library-events consumer.
/// </summary>
internal sealed partial class NightlyRefreshService(
    ICatalogReadRepository games,
    IRawgClient rawg,
    IGameImporter importer,
    IGameStatsStore stats,
    NightlyRefreshOptions options,
    TimeProvider time,
    ILogger<NightlyRefreshService> logger) : INightlyRefreshService
{
    public async Task<NightlyRefreshResult> RunAsync(CancellationToken cancellationToken)
    {
        var purged = await stats.PurgeProcessedBeforeAsync(time.GetUtcNow().UtcDateTime - options.ProcessedEventRetention, cancellationToken);

        if (!rawg.IsEnabled)
        {
            LogRawgDisabled(logger);
            return new NightlyRefreshResult(0, 0, 0, 0, false, purged);
        }

        var candidates = await games.GetRefreshCandidatesAsync(options.MaxGames, cancellationToken);
        int refreshed = 0, notFound = 0, failed = 0, consecutiveFailures = 0;
        var aborted = false;

        foreach (var rawgId in candidates)
        {
            if (refreshed + notFound + failed > 0 && options.RequestInterval > TimeSpan.Zero)
            {
                await Task.Delay(options.RequestInterval, time, cancellationToken);
            }

            try
            {
                var detail = await rawg.GetGameAsync(rawgId.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
                consecutiveFailures = 0;

                if (detail is null)
                {
                    // Removed from RAWG: keep our copy (users may own it), just stop asking every night.
                    LogNotFound(logger, rawgId);
                    notFound++;
                }
                else
                {
                    await importer.ImportAsync([detail], cancellationToken);
                    refreshed++;
                }

                await games.MarkRefreshedAsync(rawgId, time.GetUtcNow().UtcDateTime, cancellationToken);
            }
            catch (RawgUnavailableException ex)
            {
                failed++;
                LogRawgFailed(logger, ex, rawgId);
                if (++consecutiveFailures >= options.MaxConsecutiveFailures)
                {
                    aborted = true;
                    break;
                }
            }
        }

        var result = new NightlyRefreshResult(candidates.Count, refreshed, notFound, failed, aborted, purged);
        LogFinished(logger, result.Candidates, result.Refreshed, result.NotFound, result.Failed, result.Aborted, result.PurgedEvents);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nightly refresh skipped: RAWG_API_KEY is not configured")]
    private static partial void LogRawgDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "RAWG game {RawgId} no longer exists; keeping local copy")]
    private static partial void LogNotFound(ILogger logger, int rawgId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refreshing RAWG game {RawgId} failed")]
    private static partial void LogRawgFailed(ILogger logger, Exception exception, int rawgId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Nightly refresh: {Candidates} candidates, {Refreshed} refreshed, {NotFound} gone from RAWG, {Failed} failed, aborted={Aborted}; purged {Purged} processed events")]
    private static partial void LogFinished(ILogger logger, int candidates, int refreshed, int notFound, int failed, bool aborted, int purged);
}

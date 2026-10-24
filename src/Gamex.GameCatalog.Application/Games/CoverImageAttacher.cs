using Gamex.GameCatalog.Application.Abstractions;
using Gamex.GameCatalog.Domain.Games;

namespace Gamex.GameCatalog.Application.Games;

public interface ICoverImageAttacher
{
    /// <summary>Stores the uploaded cover URL on the game and publishes the change.</summary>
    Task AttachAsync(Guid gameId, string coverImageUrl, string sourceUrl, CancellationToken cancellationToken);
}

internal sealed class CoverImageAttacher(
    ICatalogRepository repository,
    ICatalogEventPublisher events,
    TimeProvider time) : ICoverImageAttacher
{
    public async Task AttachAsync(Guid gameId, string coverImageUrl, string sourceUrl, CancellationToken cancellationToken)
    {
        var game = await repository.GetGameAsync(gameId, cancellationToken);
        if (game is null)
        {
            return;
        }

        var change = game.SetCover(coverImageUrl, sourceUrl, time.GetUtcNow().UtcDateTime);
        if (change == GameChange.None)
        {
            return;
        }

        await repository.SaveChangesAsync(cancellationToken);

        if (change == GameChange.Replicated)
        {
            await events.PublishGamesAsync([game], cancellationToken);
        }
    }
}

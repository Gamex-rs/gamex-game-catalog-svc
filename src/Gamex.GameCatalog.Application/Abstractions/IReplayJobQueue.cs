using Gamex.GameCatalog.Application.Games;

namespace Gamex.GameCatalog.Application.Abstractions;

/// <summary>Runs replays in the background so the admin request returns immediately.</summary>
public interface IReplayJobQueue
{
    bool TryEnqueue(ReplayRequest request);
}

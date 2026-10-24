namespace Gamex.GameCatalog.Application.Abstractions;

public sealed record CoverImageRequest(Guid GameId, int RawgId, string SourceUrl);

/// <summary>Covers are copied from RAWG to S3 in the background so imports stay fast.</summary>
public interface ICoverImageQueue
{
    /// <returns><c>false</c> if the queue is full; the cover is picked up again by the next refresh.</returns>
    bool TryEnqueue(CoverImageRequest request);
}

using System.Threading.Channels;
using Gamex.GameCatalog.Application.Abstractions;

namespace Gamex.GameCatalog.Infrastructure.Covers;

internal sealed class CoverImageQueue : ICoverImageQueue
{
    private readonly Channel<CoverImageRequest> _channel = Channel.CreateBounded<CoverImageRequest>(
        new BoundedChannelOptions(1000) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });

    public ChannelReader<CoverImageRequest> Reader => _channel.Reader;

    public bool TryEnqueue(CoverImageRequest request) => _channel.Writer.TryWrite(request);
}

/// <summary>Used when S3 is not configured: covers are never copied.</summary>
internal sealed class DisabledCoverImageQueue : ICoverImageQueue
{
    public bool TryEnqueue(CoverImageRequest request) => true;
}

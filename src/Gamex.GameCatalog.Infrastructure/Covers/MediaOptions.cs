namespace Gamex.GameCatalog.Infrastructure.Covers;

internal sealed class MediaOptions
{
    /// <summary>S3 bucket holding game covers (<c>S3_MEDIA_BUCKET</c>). Covers are disabled when empty.</summary>
    public string? Bucket { get; set; }

    /// <summary>Key prefix per environment, e.g. <c>dev/</c> (<c>S3_MEDIA_PREFIX</c>); the bucket is shared between environments.</summary>
    public string Prefix { get; set; } = "";

    /// <summary>Cloudflare base URL that proxies the bucket (<c>CLOUDFLARE_MEDIA_BASE_URL</c>).</summary>
    public string? CdnBaseUrl { get; set; }

    /// <summary>Covers larger than this are skipped.</summary>
    public long MaxCoverBytes { get; set; } = 10 * 1024 * 1024;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(Bucket) && !string.IsNullOrWhiteSpace(CdnBaseUrl);
}

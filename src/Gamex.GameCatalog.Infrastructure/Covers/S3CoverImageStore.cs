using System.Security.Cryptography;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Gamex.GameCatalog.Application.Abstractions;

namespace Gamex.GameCatalog.Infrastructure.Covers;

/// <summary>Downloads a cover from RAWG and stores it at <c>{prefix}game-covers/{rawgId}.jpg</c>.</summary>
internal sealed class S3CoverImageStore(IHttpClientFactory httpClients, IAmazonS3 s3, MediaOptions options)
{
    public const string SourceHttpClient = "cover-source";

    /// <returns>The CDN URL of the stored cover.</returns>
    public async Task<string> CopyAsync(CoverImageRequest request, CancellationToken ct)
    {
        var http = httpClients.CreateClient(SourceHttpClient);
        using var response = await http.GetAsync(new Uri(request.SourceUrl), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > options.MaxCoverBytes)
        {
            throw new InvalidOperationException($"Cover is larger than {options.MaxCoverBytes} bytes.");
        }

        // S3 needs the length up front; covers are small, so buffer them.
        using var buffer = new MemoryStream();
        await response.Content.CopyToAsync(buffer, ct);
        if (buffer.Length > options.MaxCoverBytes)
        {
            throw new InvalidOperationException($"Cover is larger than {options.MaxCoverBytes} bytes.");
        }

        buffer.Position = 0;

        var key = $"{options.Prefix}game-covers/{request.RawgId}.jpg";
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = options.Bucket,
            Key = key,
            InputStream = buffer,
            AutoCloseStream = false,
            ContentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg",
            Headers = { CacheControl = "public, max-age=31536000, immutable" },
        }, ct);

        // The key is stable per game, so a version derived from the source busts the CDN cache when RAWG's cover changes.
        return $"{options.CdnBaseUrl!.TrimEnd('/')}/{key}?v={Version(request.SourceUrl)}";
    }

    private static string Version(string sourceUrl) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sourceUrl)))[..12];
}

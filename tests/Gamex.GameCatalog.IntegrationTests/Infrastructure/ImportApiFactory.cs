using System.Collections.Concurrent;
using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Gamex.GameCatalog.Application.Abstractions;
using Gamex.GameCatalog.Application.Rawg;
using Gamex.GameCatalog.Domain.Games;
using Gamex.GameCatalog.Infrastructure.Covers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.LocalStack;

namespace Gamex.GameCatalog.IntegrationTests.Infrastructure;

/// <summary>API with a fake RAWG, LocalStack S3 for covers and a recording event publisher.</summary>
public sealed class ImportApiFactory : ApiFactoryBase
{
    public const string Bucket = "gamex-media";
    public const string CdnBaseUrl = "https://media.gamex.test";
    public const string MediaPrefix = "test/";
    public static readonly byte[] CoverBytes = "fake-jpeg-bytes"u8.ToArray();

    private readonly LocalStackContainer _localStack = new LocalStackBuilder("localstack/localstack:3").Build();

    public FakeRawg Rawg { get; } = new();
    public RecordingPublisher Events { get; } = new();

    public override async ValueTask InitializeAsync()
    {
        await Task.WhenAll(base.InitializeAsync().AsTask(), _localStack.StartAsync());
        using var s3 = CreateS3Client();
        await s3.PutBucketAsync(Bucket);
    }

    public AmazonS3Client CreateS3Client() => new(
        new BasicAWSCredentials("test", "test"),
        new AmazonS3Config { ServiceURL = _localStack.GetConnectionString(), ForcePathStyle = true });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("S3_MEDIA_BUCKET", Bucket);
        builder.UseSetting("S3_MEDIA_PREFIX", MediaPrefix);
        builder.UseSetting("CLOUDFLARE_MEDIA_BASE_URL", CdnBaseUrl);
        builder.UseSetting("AWS_SERVICE_URL", _localStack.GetConnectionString());

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IRawgClient>(Rawg);
            services.AddSingleton<ICatalogEventPublisher>(Events);
            services.AddHttpClient(S3CoverImageStore.SourceHttpClient)
                .ConfigurePrimaryHttpMessageHandler(() => new CoverSourceHandler());
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _localStack.DisposeAsync();
    }

    /// <summary>Serves <see cref="CoverBytes"/> for every RAWG image URL.</summary>
    private sealed class CoverSourceHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(CoverBytes);
            content.Headers.ContentType = new("image/jpeg");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}

/// <summary>Search results keyed by term; terms in <see cref="FailingTerms"/> throw.</summary>
public sealed class FakeRawg : IRawgClient
{
    public ConcurrentDictionary<string, IReadOnlyList<RawgGame>> Results { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, bool> FailingTerms { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentQueue<string> Searches { get; } = new();

    public bool IsEnabled => true;

    public Task<IReadOnlyList<RawgGame>> SearchAsync(string term, int pageSize, CancellationToken cancellationToken)
    {
        Searches.Enqueue(term);
        if (FailingTerms.ContainsKey(term))
        {
            throw new RawgUnavailableException("RAWG is down");
        }

        return Task.FromResult(Results.GetValueOrDefault(term) ?? []);
    }

    public Task<RawgGame?> GetGameAsync(string idOrSlug, CancellationToken cancellationToken) =>
        Task.FromResult(Results.Values.SelectMany(r => r).FirstOrDefault(g => g.Slug == idOrSlug));
}

public sealed class RecordingPublisher : ICatalogEventPublisher
{
    public ConcurrentQueue<(string Type, Guid Id)> Published { get; } = new();

    public Task PublishPlatformsAsync(IReadOnlyCollection<Platform> platforms, CancellationToken cancellationToken)
    {
        foreach (var p in platforms)
        {
            Published.Enqueue(("catalog.platform.upserted", p.Id));
        }

        return Task.CompletedTask;
    }

    public Task PublishGamesAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken)
    {
        foreach (var g in games)
        {
            Published.Enqueue(("catalog.game.upserted", g.Id));
        }

        return Task.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class ImportApiFixture : ICollectionFixture<ImportApiFactory>
{
    public const string Name = "ImportApi";
}

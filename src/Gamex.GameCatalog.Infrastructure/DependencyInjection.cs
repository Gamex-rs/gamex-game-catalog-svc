using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Gamex.GameCatalog.Application.Abstractions;
using Gamex.GameCatalog.Application.Games;
using Gamex.GameCatalog.Application.Rawg;
using Gamex.GameCatalog.Infrastructure.Covers;
using Gamex.GameCatalog.Infrastructure.Events;
using Gamex.GameCatalog.Infrastructure.Persistence;
using Gamex.GameCatalog.Infrastructure.Persistence.Queries;
using Gamex.GameCatalog.Infrastructure.Rawg;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Gamex.GameCatalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["DB_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("DB_CONNECTION_STRING is not configured.");

        services.AddDbContext<CatalogDbContext>(options => options.UseMySQL(connectionString));
        services.AddScoped<IGameQueries, GameQueries>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddSingleton(TimeProvider.System);
        services.AddHealthChecks().AddDbContextCheck<CatalogDbContext>("database");

        services.AddSingleton<ICatalogEventPublisher, LoggingCatalogEventPublisher>();

        services.AddRawg(configuration);
        services.AddCovers(configuration);

        return services;
    }

    private static void AddRawg(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new RawgOptions { ApiKey = configuration["RAWG_API_KEY"] };
        if (configuration["RAWG_BASE_URL"] is { Length: > 0 } baseUrl)
        {
            options.BaseUrl = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        }

        services.AddSingleton(options);
        services.AddHttpClient<IRawgClient, RawgClient>(http => http.BaseAddress = options.BaseUrl)
            .AddStandardResilienceHandler(resilience =>
            {
                // Search is on the autocomplete path, so fail fast and fall back to local results.
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                resilience.Retry.MaxRetryAttempts = 1;
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(7);
            });
    }

    private static void AddCovers(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new MediaOptions
        {
            Bucket = configuration["S3_MEDIA_BUCKET"],
            Prefix = configuration["S3_MEDIA_PREFIX"] ?? "",
            CdnBaseUrl = configuration["CLOUDFLARE_MEDIA_BASE_URL"],
        };

        if (!options.IsEnabled)
        {
            services.AddSingleton<ICoverImageQueue, DisabledCoverImageQueue>();
            return;
        }

        services.AddSingleton(options);
        services.AddSingleton<IAmazonS3>(_ => CreateS3Client(configuration));
        services.AddHttpClient(S3CoverImageStore.SourceHttpClient, http => http.Timeout = TimeSpan.FromSeconds(30))
            .AddStandardResilienceHandler();
        services.AddSingleton<S3CoverImageStore>();
        services.AddSingleton<CoverImageQueue>();
        services.AddSingleton<ICoverImageQueue>(sp => sp.GetRequiredService<CoverImageQueue>());
        services.AddHostedService<CoverImageWorker>();
    }

    private static AmazonS3Client CreateS3Client(IConfiguration configuration)
    {
        var config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(configuration["AWS_DEFAULT_REGION"] ?? "eu-central-1"),
        };

        // Local development and tests point at LocalStack, which accepts any credentials.
        if (configuration["AWS_SERVICE_URL"] is { Length: > 0 } serviceUrl)
        {
            config.ServiceURL = serviceUrl;
            config.ForcePathStyle = true;
            return new AmazonS3Client(new BasicAWSCredentials("test", "test"), config);
        }

        return new AmazonS3Client(config);
    }
}

using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SQS;
using Gamex.GameCatalog.Application.Abstractions;
using Gamex.GameCatalog.Application.Games;
using Gamex.GameCatalog.Application.Rawg;
using Gamex.GameCatalog.Application.Stats;
using Gamex.GameCatalog.Infrastructure.Covers;
using Gamex.GameCatalog.Infrastructure.Events;
using Gamex.GameCatalog.Infrastructure.Messaging;
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

        services.AddScoped<ICatalogReadRepository, CatalogReadRepository>();

        services.AddScoped<IGameStatsStore, GameStatsStore>();

        services.AddMessaging(configuration);
        services.AddSingleton<ReplayJobQueue>();
        services.AddSingleton<IReplayJobQueue>(sp => sp.GetRequiredService<ReplayJobQueue>());
        services.AddHostedService<ReplayWorker>();

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
        services.AddSingleton<IAmazonS3>(_ => CreateAwsClient(configuration, (credentials, config) => credentials is null ? new AmazonS3Client(config) : new AmazonS3Client(credentials, config), new AmazonS3Config()));
        services.AddHttpClient(S3CoverImageStore.SourceHttpClient, http => http.Timeout = TimeSpan.FromSeconds(30))
            .AddStandardResilienceHandler();
        services.AddSingleton<S3CoverImageStore>();
        services.AddSingleton<CoverImageQueue>();
        services.AddSingleton<ICoverImageQueue>(sp => sp.GetRequiredService<CoverImageQueue>());
        services.AddHostedService<CoverImageWorker>();
    }

    private static void AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new MessagingOptions
        {
            CatalogEventsQueueUrl = configuration["SQS_CATALOG_EVENTS_URL"],
            LibraryEventsQueueUrl = configuration["SQS_LIBRARY_EVENTS_URL"],
        };
        if (int.TryParse(configuration["SQS_RETRY_DELAY_SECONDS"], out var retryDelay))
        {
            options.RetryDelaySeconds = retryDelay;
        }

        services.AddSingleton(options);

        if (string.IsNullOrWhiteSpace(options.CatalogEventsQueueUrl) && string.IsNullOrWhiteSpace(options.LibraryEventsQueueUrl))
        {
            services.AddSingleton<ICatalogEventPublisher, LoggingCatalogEventPublisher>();
            return;
        }

        services.AddSingleton<IAmazonSQS>(_ => CreateAwsClient(
            configuration,
            (credentials, config) => credentials is null ? new AmazonSQSClient(config) : new AmazonSQSClient(credentials, config),
            new AmazonSQSConfig()));

        if (string.IsNullOrWhiteSpace(options.CatalogEventsQueueUrl))
        {
            services.AddSingleton<ICatalogEventPublisher, LoggingCatalogEventPublisher>();
        }
        else
        {
            services.AddSingleton<ICatalogEventPublisher, SqsCatalogEventPublisher>();
        }

        if (!string.IsNullOrWhiteSpace(options.LibraryEventsQueueUrl))
        {
            services.AddHostedService<LibraryEventsConsumer>();
        }
    }

    /// <summary>
    /// Real AWS uses the default credential chain (ECS task role). Local development and tests set <c>AWS_SERVICE_URL</c>
    /// to LocalStack, which accepts any credentials.
    /// </summary>
    private static TClient CreateAwsClient<TClient, TConfig>(
        IConfiguration configuration,
        Func<AWSCredentials?, TConfig, TClient> create,
        TConfig config)
        where TConfig : ClientConfig
    {
        config.RegionEndpoint = RegionEndpoint.GetBySystemName(configuration["AWS_DEFAULT_REGION"] ?? "eu-central-1");

        if (configuration["AWS_SERVICE_URL"] is { Length: > 0 } serviceUrl)
        {
            config.ServiceURL = serviceUrl;
            if (config is AmazonS3Config s3)
            {
                s3.ForcePathStyle = true;
            }

            return create(new BasicAWSCredentials("test", "test"), config);
        }

        return create(null, config);
    }
}

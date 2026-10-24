using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Gamex.GameCatalog.Application.Rawg;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.LocalStack;

namespace Gamex.GameCatalog.IntegrationTests.Infrastructure;

/// <summary>API with the real SQS publisher and library-events consumer against LocalStack SQS.</summary>
public sealed class MessagingApiFactory : ApiFactoryBase
{
    private readonly LocalStackContainer _localStack = new LocalStackBuilder("localstack/localstack:3").Build();

    public FakeRawg Rawg { get; } = new();
    public string CatalogEventsUrl { get; private set; } = "";
    public string LibraryEventsUrl { get; private set; } = "";
    public string LibraryEventsDlqUrl { get; private set; } = "";

    public override async ValueTask InitializeAsync()
    {
        await Task.WhenAll(base.InitializeAsync().AsTask(), _localStack.StartAsync());

        using var sqs = CreateSqsClient();
        CatalogEventsUrl = (await sqs.CreateQueueAsync("gamex-catalog-events")).QueueUrl;
        LibraryEventsDlqUrl = (await sqs.CreateQueueAsync("gamex-library-events-dlq")).QueueUrl;
        var dlqArn = (await sqs.GetQueueAttributesAsync(LibraryEventsDlqUrl, ["QueueArn"])).QueueARN;
        LibraryEventsUrl = (await sqs.CreateQueueAsync(new CreateQueueRequest
        {
            QueueName = "gamex-library-events",
            Attributes = new Dictionary<string, string>
            {
                // Short timeout so unprocessable messages are redelivered quickly in tests.
                ["VisibilityTimeout"] = "1",
                ["RedrivePolicy"] = $$"""{"deadLetterTargetArn":"{{dlqArn}}","maxReceiveCount":"3"}""",
            },
        })).QueueUrl;
    }

    public AmazonSQSClient CreateSqsClient() => new(
        new BasicAWSCredentials("test", "test"),
        new AmazonSQSConfig
        {
            ServiceURL = _localStack.GetConnectionString(),
            // Same signing region as the service, so both see the same LocalStack queues.
            AuthenticationRegion = "eu-central-1",
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("AWS_SERVICE_URL", _localStack.GetConnectionString());
        builder.UseSetting("SQS_CATALOG_EVENTS_URL", CatalogEventsUrl);
        builder.UseSetting("SQS_LIBRARY_EVENTS_URL", LibraryEventsUrl);
        builder.UseSetting("SQS_RETRY_DELAY_SECONDS", "1");
        builder.ConfigureTestServices(services => services.AddSingleton<IRawgClient>(Rawg));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _localStack.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class MessagingApiFixture : ICollectionFixture<MessagingApiFactory>
{
    public const string Name = "MessagingApi";
}

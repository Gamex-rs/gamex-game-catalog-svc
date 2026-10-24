namespace Gamex.GameCatalog.Infrastructure.Messaging;

internal sealed class MessagingOptions
{
    /// <summary>Queue this service publishes to (<c>SQS_CATALOG_EVENTS_URL</c>). Events are only logged when empty.</summary>
    public string? CatalogEventsQueueUrl { get; set; }

    /// <summary>Queue this service consumes (<c>SQS_LIBRARY_EVENTS_URL</c>). The consumer does not run when empty.</summary>
    public string? LibraryEventsQueueUrl { get; set; }

    /// <summary>How long a message whose game is not yet known stays hidden before redelivery (<c>SQS_RETRY_DELAY_SECONDS</c>).</summary>
    public int RetryDelaySeconds { get; set; } = 30;
}

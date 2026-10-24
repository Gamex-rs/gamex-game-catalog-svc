using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Gamex.GameCatalog.Application.Abstractions;
using Gamex.GameCatalog.Domain.Games;
using Microsoft.Extensions.Logging;

namespace Gamex.GameCatalog.Infrastructure.Messaging;

/// <summary>
/// Publishes to <c>gamex-catalog-events</c>. Failures are logged, not thrown: the database change is already saved,
/// and the game is republished on its next change, by the nightly refresh or by a replay.
/// </summary>
internal sealed partial class SqsCatalogEventPublisher(
    IAmazonSQS sqs,
    MessagingOptions options,
    ILogger<SqsCatalogEventPublisher> logger) : ICatalogEventPublisher
{
    private const int MaxBatchSize = 10;

    public Task PublishPlatformsAsync(IReadOnlyCollection<Platform> platforms, CancellationToken cancellationToken) =>
        SendAsync(
            platforms.Select(p => (p.Id, CatalogEventMessages.PlatformUpserted,
                JsonSerializer.Serialize(CatalogEventMessages.From(p), CatalogEventMessages.JsonOptions))),
            cancellationToken);

    public Task PublishGamesAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken) =>
        SendAsync(
            games.Select(g => (g.Id, CatalogEventMessages.GameUpserted,
                JsonSerializer.Serialize(CatalogEventMessages.From(g), CatalogEventMessages.JsonOptions))),
            cancellationToken);

    private async Task SendAsync(IEnumerable<(Guid Id, string EventType, string Body)> messages, CancellationToken ct)
    {
        foreach (var batch in messages.Chunk(MaxBatchSize))
        {
            var request = new SendMessageBatchRequest
            {
                QueueUrl = options.CatalogEventsQueueUrl,
                Entries = batch.Select((m, i) => new SendMessageBatchRequestEntry
                {
                    Id = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    MessageBody = m.Body,
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>
                    {
                        ["eventType"] = new() { DataType = "String", StringValue = m.EventType },
                    },
                }).ToList(),
            };

            try
            {
                var response = await sqs.SendMessageBatchAsync(request, ct);
                foreach (var failed in response.Failed ?? [])
                {
                    var entity = batch[int.Parse(failed.Id, System.Globalization.CultureInfo.InvariantCulture)];
                    LogSendFailed(logger, null, entity.EventType, entity.Id, failed.Message);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (AmazonSQSException ex)
            {
                foreach (var entity in batch)
                {
                    LogSendFailed(logger, ex, entity.EventType, entity.Id, ex.Message);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Publishing {EventType} for {Id} failed: {Reason}")]
    private static partial void LogSendFailed(ILogger logger, Exception? exception, string eventType, Guid id, string reason);
}

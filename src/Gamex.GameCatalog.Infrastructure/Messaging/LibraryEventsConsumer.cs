using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Gamex.GameCatalog.Application.Stats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gamex.GameCatalog.Infrastructure.Messaging;

/// <summary>
/// Consumes <c>library.game.status.changed</c> from <c>gamex-library-events</c> and updates <c>game_stats</c>.
/// Messages that cannot be applied are left on the queue; after the redrive policy's max receive count (3)
/// SQS moves them to <c>gamex-library-events-dlq</c>.
/// </summary>
internal sealed partial class LibraryEventsConsumer(
    IAmazonSQS sqs,
    MessagingOptions options,
    IServiceScopeFactory scopes,
    ILogger<LibraryEventsConsumer> logger) : BackgroundService
{
    public const string EventType = "library.game.status.changed";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = options.LibraryEventsQueueUrl,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = 20,
                    MessageSystemAttributeNames = ["ApproximateReceiveCount"],
                }, stoppingToken);

                foreach (var message in response.Messages ?? [])
                {
                    await HandleAsync(message, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Keep polling; unhandled messages become visible again and are retried.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogPollFailed(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    internal async Task HandleAsync(Message message, CancellationToken ct)
    {
        var parsed = Parse(message.Body);
        if (parsed is null)
        {
            // Poison message: leave it so the redrive policy moves it to the DLQ for inspection.
            LogInvalidMessage(logger, message.MessageId);
            return;
        }

        var (gameId, delta) = parsed.Value;
        if (delta.IsZero)
        {
            await DeleteAsync(message, ct);
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGameStatsStore>();
        var outcome = await store.ApplyAsync(message.MessageId, gameId, delta, ct);

        switch (outcome)
        {
            case StatsUpdateOutcome.Applied:
            case StatsUpdateOutcome.Duplicate:
                await DeleteAsync(message, ct);
                break;

            case StatsUpdateOutcome.GameNotFound:
                // The game may still be importing; hide the message for a while and let SQS redeliver it.
                LogGameNotFound(logger, gameId, message.MessageId, ReceiveCount(message));
                await sqs.ChangeMessageVisibilityAsync(options.LibraryEventsQueueUrl, message.ReceiptHandle, options.RetryDelaySeconds, ct);
                break;
        }
    }

    internal static (Guid GameId, StatsDelta Delta)? Parse(string body)
    {
        LibraryStatusChangedMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<LibraryStatusChangedMessage>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (message is not { EventType: EventType, Delta: { } d } || message.GameId == Guid.Empty)
        {
            return null;
        }

        var delta = new StatsDelta(d.Owned, d.Wishlist, d.ForExchange, d.ForSale, d.WantToBuy);
        return delta.IsValid ? (message.GameId, delta) : null;
    }

    private async Task DeleteAsync(Message message, CancellationToken ct) =>
        await sqs.DeleteMessageAsync(options.LibraryEventsQueueUrl, message.ReceiptHandle, ct);

    private static string ReceiveCount(Message message) =>
        message.Attributes?.GetValueOrDefault("ApproximateReceiveCount") ?? "?";

    private sealed record LibraryStatusChangedMessage(string? EventType, Guid GameId, Guid UserId, DeltaJson? Delta, DateTime OccurredAt);

    private sealed record DeltaJson(int Owned, int Wishlist, int ForExchange, int ForSale, int WantToBuy);

    [LoggerMessage(Level = LogLevel.Error, Message = "Polling gamex-library-events failed")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Invalid library event {MessageId}; leaving it for the DLQ")]
    private static partial void LogInvalidMessage(ILogger logger, string messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Game {GameId} not found for library event {MessageId} (receive {ReceiveCount}); retrying later")]
    private static partial void LogGameNotFound(ILogger logger, Guid gameId, string messageId, string receiveCount);
}

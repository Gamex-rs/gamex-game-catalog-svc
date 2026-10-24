using Gamex.GameCatalog.Infrastructure.Messaging;

namespace Gamex.GameCatalog.UnitTests.Messaging;

public sealed class LibraryEventsConsumerTests
{
    private static readonly Guid GameId = Guid.Parse("0192f3a1-0000-7000-8000-000000000001");

    private static string Message(string eventType = "library.game.status.changed", string gameId = "0192f3a1-0000-7000-8000-000000000001", string delta = """{"owned":1,"wishlist":0,"forExchange":0,"forSale":-1,"wantToBuy":0}""") =>
        $$"""{"eventType":"{{eventType}}","gameId":"{{gameId}}","userId":"0192f3a1-0000-7000-8000-000000000099","delta":{{delta}},"occurredAt":"2026-05-12T10:00:00Z"}""";

    [Fact]
    public void Parse_ValidMessage()
    {
        var parsed = LibraryEventsConsumer.Parse(Message());

        parsed.ShouldNotBeNull();
        parsed.Value.GameId.ShouldBe(GameId);
        parsed.Value.Delta.Owned.ShouldBe(1);
        parsed.Value.Delta.ForSale.ShouldBe(-1);
        parsed.Value.Delta.IsZero.ShouldBeFalse();
    }

    [Fact]
    public void Parse_ZeroDelta_IsValidAndZero()
    {
        var parsed = LibraryEventsConsumer.Parse(Message(delta: """{"owned":0,"wishlist":0,"forExchange":0,"forSale":0,"wantToBuy":0}"""));

        parsed.ShouldNotBeNull().Delta.IsZero.ShouldBeTrue();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"eventType":"library.game.status.changed"}""")]
    public void Parse_Malformed_ReturnsNull(string body)
    {
        LibraryEventsConsumer.Parse(body).ShouldBeNull();
    }

    [Fact]
    public void Parse_WrongEventType_ReturnsNull()
    {
        LibraryEventsConsumer.Parse(Message(eventType: "listing.created")).ShouldBeNull();
    }

    [Fact]
    public void Parse_EmptyGameId_ReturnsNull()
    {
        LibraryEventsConsumer.Parse(Message(gameId: Guid.Empty.ToString())).ShouldBeNull();
    }

    [Fact]
    public void Parse_DeltaOutOfRange_ReturnsNull()
    {
        LibraryEventsConsumer.Parse(Message(delta: """{"owned":2,"wishlist":0,"forExchange":0,"forSale":0,"wantToBuy":0}""")).ShouldBeNull();
    }
}

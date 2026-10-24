using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gamex.GameCatalog.Domain.Games;

namespace Gamex.GameCatalog.Infrastructure.Messaging;

/// <summary>Wire format of <c>gamex-catalog-events</c> (see spec §6.1).</summary>
internal static class CatalogEventMessages
{
    public const string PlatformUpserted = "catalog.platform.upserted";
    public const string GameUpserted = "catalog.game.upserted";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new MicrosecondUtcConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static PlatformUpsertedMessage From(Platform p) =>
        new(PlatformUpserted, p.Id, p.Name, p.Slug, p.UpdatedAt);

    public static GameUpsertedMessage From(Game g) => new(
        GameUpserted,
        g.Id,
        g.Title,
        g.CoverImageUrl,
        g.ReleaseDate,
        g.Metacritic,
        g.Platforms.Select(p => p.Id).Order().ToList(),
        g.UpdatedAt);

    /// <summary>Writes <c>2026-10-24T10:00:00.123456Z</c>: exactly the precision stored in MySQL and compared by consumers.</summary>
    private sealed class MicrosecondUtcConverter : JsonConverter<DateTime>
    {
        private const string Format = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
    }
}

internal sealed record PlatformUpsertedMessage(
    string EventType,
    Guid PlatformId,
    string Name,
    string Slug,
    DateTime UpdatedAt);

internal sealed record GameUpsertedMessage(
    string EventType,
    Guid GameId,
    string Title,
    string? CoverImageUrl,
    DateOnly? ReleaseDate,
    short? Metacritic,
    IReadOnlyList<Guid> PlatformIds,
    DateTime UpdatedAt);

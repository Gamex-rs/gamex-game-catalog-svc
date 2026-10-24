using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Gamex.GameCatalog.Application.Rawg;

namespace Gamex.GameCatalog.Infrastructure.Rawg;

internal sealed class RawgOptions
{
    public const string DefaultBaseUrl = "https://api.rawg.io/api/";

    public string? ApiKey { get; set; }
    public Uri BaseUrl { get; set; } = new(DefaultBaseUrl);
}

/// <summary>Typed client for the RAWG REST API (https://api.rawg.io/docs/).</summary>
internal sealed class RawgClient(HttpClient http, RawgOptions options) : IRawgClient
{
    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.ApiKey);

    public async Task<IReadOnlyList<RawgGame>> SearchAsync(string term, int pageSize, CancellationToken cancellationToken)
    {
        var uri = $"games?key={Uri.EscapeDataString(options.ApiKey ?? "")}&search={Uri.EscapeDataString(term)}&page_size={pageSize}";
        var page = await SendAsync<RawgPage>(uri, cancellationToken);
        return page?.Results.Select(Map).ToList() ?? [];
    }

    public async Task<RawgGame?> GetGameAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        var uri = $"games/{Uri.EscapeDataString(idOrSlug)}?key={Uri.EscapeDataString(options.ApiKey ?? "")}";
        var game = await SendAsync<RawgGameJson>(uri, cancellationToken);
        return game is null ? null : Map(game);
    }

    private async Task<T?> SendAsync<T>(string relativeUri, CancellationToken ct)
        where T : class
    {
        if (!IsEnabled)
        {
            throw new RawgUnavailableException("RAWG_API_KEY is not configured.");
        }

        try
        {
            using var response = await http.GetAsync(new Uri(relativeUri, UriKind.Relative), ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // Never leak the request URI: it contains the API key.
            throw new RawgUnavailableException($"RAWG request failed: {ex.GetType().Name}", ex);
        }
    }

    private static RawgGame Map(RawgGameJson g) => new(
        g.Id,
        g.Slug,
        g.Name,
        string.IsNullOrWhiteSpace(g.DescriptionRaw) ? null : g.DescriptionRaw.Trim(),
        ParseDate(g.Released),
        g.Metacritic is { } m ? (short)m : null,
        string.IsNullOrWhiteSpace(g.BackgroundImage) ? null : g.BackgroundImage,
        g.Platforms?.Select(p => new RawgRef(p.Platform.Name, p.Platform.Slug)).ToList() ?? [],
        g.Genres?.Select(x => new RawgRef(x.Name, x.Slug)).ToList() ?? []);

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private sealed record RawgPage([property: JsonPropertyName("results")] List<RawgGameJson> Results);

    private sealed record RawgGameJson(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("slug")] string Slug,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description_raw")] string? DescriptionRaw,
        [property: JsonPropertyName("released")] string? Released,
        [property: JsonPropertyName("metacritic")] int? Metacritic,
        [property: JsonPropertyName("background_image")] string? BackgroundImage,
        [property: JsonPropertyName("platforms")] List<RawgPlatformEntry>? Platforms,
        [property: JsonPropertyName("genres")] List<RawgNamed>? Genres);

    private sealed record RawgPlatformEntry([property: JsonPropertyName("platform")] RawgNamed Platform);

    private sealed record RawgNamed(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("slug")] string Slug);
}

using System.Net;
using Gamex.GameCatalog.Application.Rawg;
using Gamex.GameCatalog.Infrastructure.Rawg;
using Gamex.GameCatalog.UnitTests.Fakes;

namespace Gamex.GameCatalog.UnitTests.Rawg;

public sealed class RawgClientTests
{
    private const string ApiKey = "secret-key";

    private const string SearchJson = """
        {
          "count": 2,
          "results": [
            {
              "id": 326243,
              "slug": "elden-ring",
              "name": "Elden Ring",
              "released": "2022-02-25",
              "background_image": "https://media.rawg.io/media/games/elden.jpg",
              "metacritic": 94,
              "platforms": [
                { "platform": { "id": 187, "name": "PlayStation 5", "slug": "playstation5" } },
                { "platform": { "id": 4, "name": "PC", "slug": "pc" } }
              ],
              "genres": [ { "id": 4, "name": "Action", "slug": "action" } ]
            },
            {
              "id": 999,
              "slug": "unannounced",
              "name": "Unannounced",
              "released": null,
              "tba": true,
              "background_image": "",
              "metacritic": null,
              "platforms": null,
              "genres": []
            }
          ]
        }
        """;

    private static (RawgClient Client, StubHttpHandler Handler) Create(StubHttpHandler handler, string? apiKey = ApiKey)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.rawg.test/api/") };
        return (new RawgClient(http, new RawgOptions { ApiKey = apiKey }), handler);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_MapsResults()
    {
        var (client, _) = Create(StubHttpHandler.Json(SearchJson));

        var games = await client.SearchAsync("elden", 10, Ct);

        games.Count.ShouldBe(2);
        var elden = games[0];
        elden.Id.ShouldBe(326243);
        elden.Slug.ShouldBe("elden-ring");
        elden.Name.ShouldBe("Elden Ring");
        elden.Description.ShouldBeNull();
        elden.Released.ShouldBe(new DateOnly(2022, 2, 25));
        elden.Metacritic.ShouldBe((short)94);
        elden.BackgroundImage.ShouldBe("https://media.rawg.io/media/games/elden.jpg");
        elden.Platforms.ShouldBe([new RawgRef("PlayStation 5", "playstation5"), new RawgRef("PC", "pc")]);
        elden.Genres.ShouldBe([new RawgRef("Action", "action")]);

        var tba = games[1];
        tba.Released.ShouldBeNull();
        tba.Metacritic.ShouldBeNull();
        tba.BackgroundImage.ShouldBeNull();
        tba.Platforms.ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_SendsKeyTermAndPageSize()
    {
        var (client, handler) = Create(StubHttpHandler.Json("""{ "results": [] }"""));

        await client.SearchAsync("zelda & link", 10, Ct);

        handler.Requests.ShouldHaveSingleItem().AbsoluteUri
            .ShouldBe("https://api.rawg.test/api/games?key=secret-key&search=zelda%20%26%20link&page_size=10");
    }

    [Fact]
    public async Task GetGame_MapsDescription()
    {
        var (client, handler) = Create(StubHttpHandler.Json("""
            { "id": 1, "slug": "hades", "name": "Hades", "description_raw": "  Defy the god of the dead.  ", "platforms": [], "genres": [] }
            """));

        var game = await client.GetGameAsync("hades", Ct);

        game.ShouldNotBeNull().Description.ShouldBe("Defy the god of the dead.");
        handler.Requests.Single().AbsolutePath.ShouldBe("/api/games/hades");
    }

    [Fact]
    public async Task GetGame_NotFound_ReturnsNull()
    {
        var (client, _) = Create(StubHttpHandler.Json("""{ "detail": "Not found." }""", HttpStatusCode.NotFound));

        (await client.GetGameAsync("nope", Ct)).ShouldBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{}""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task Failure_ThrowsRawgUnavailableWithoutLeakingKey(HttpStatusCode status, string body)
    {
        var (client, _) = Create(StubHttpHandler.Json(body, status));

        var ex = await Should.ThrowAsync<RawgUnavailableException>(() => client.SearchAsync("elden", 10, Ct));

        ex.Message.ShouldNotContain(ApiKey);
    }

    [Fact]
    public async Task NoApiKey_IsDisabledAndThrows()
    {
        var (client, handler) = Create(StubHttpHandler.Json("{}"), apiKey: null);

        client.IsEnabled.ShouldBeFalse();
        await Should.ThrowAsync<RawgUnavailableException>(() => client.SearchAsync("elden", 10, Ct));
        handler.Requests.ShouldBeEmpty();
    }
}

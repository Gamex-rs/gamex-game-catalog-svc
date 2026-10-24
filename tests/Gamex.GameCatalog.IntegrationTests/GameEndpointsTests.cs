using System.Net;
using System.Net.Http.Json;
using Gamex.GameCatalog.Application.Games;
using Gamex.GameCatalog.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Gamex.GameCatalog.IntegrationTests;

[Collection(CatalogApiFixture.Name)]
public sealed class GameEndpointsTests(CatalogApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private TestCatalog Catalog => factory.Catalog;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<PagedResult<GameSummaryDto>> ListAsync(string query)
    {
        var result = await _client.GetFromJsonAsync<PagedResult<GameSummaryDto>>($"/api/v1/games{query}", Ct);
        return result.ShouldNotBeNull();
    }

    [Fact]
    public async Task List_Default_ReturnsAllGamesSortedByTitle()
    {
        var page = await ListAsync("");

        page.TotalCount.ShouldBe(5);
        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(20);
        page.Items.Select(g => g.Title).ShouldBe(
        [
            "Elden Ring",
            "Elden Ring Nightreign",
            "Hades",
            "Stardew Valley",
            "The Legend of Zelda: Tears of the Kingdom",
        ]);
    }

    [Fact]
    public async Task List_IncludesPlatformsAndStats()
    {
        var page = await ListAsync("?q=hades");

        var hades = page.Items.ShouldHaveSingleItem();
        hades.Id.ShouldBe(Catalog.Hades.Id);
        hades.Platforms.Select(p => p.Slug).ShouldBe(["nintendo-switch", "pc", "playstation5"]);
        hades.Stats.OwnedCount.ShouldBe(300u);
        hades.CoverImageUrl.ShouldBe("https://media.gamex.rs/game-covers/1003.jpg");
    }

    [Fact]
    public async Task List_TitleSearch_IsCaseInsensitivePartialMatch()
    {
        var page = await ListAsync("?q=ELDEN");

        page.Items.Select(g => g.Id).ShouldBe([Catalog.EldenRing.Id, Catalog.Nightreign.Id]);
    }

    [Fact]
    public async Task List_PlatformFilter_MatchesAnyOfThePlatforms()
    {
        var switchOnly = await ListAsync($"?platformIds={Catalog.Switch.Id}");
        switchOnly.Items.Select(g => g.Id).ShouldBe([Catalog.Hades.Id, Catalog.Stardew.Id, Catalog.Zelda.Id]);

        var switchOrPs5 = await ListAsync($"?platformIds={Catalog.Switch.Id}&platformIds={Catalog.Ps5.Id}");
        switchOrPs5.TotalCount.ShouldBe(5);
    }

    [Fact]
    public async Task List_GenreFilter()
    {
        var page = await ListAsync($"?genreId={Catalog.Rpg.Id}");

        page.Items.Select(g => g.Id).ShouldBe([Catalog.EldenRing.Id, Catalog.Hades.Id, Catalog.Stardew.Id]);
    }

    [Fact]
    public async Task List_SortByOwnedCountDesc()
    {
        var page = await ListAsync("?sortBy=ownedCount&sortDir=desc&pageSize=3");

        page.Items.Select(g => g.Id).ShouldBe([Catalog.Hades.Id, Catalog.EldenRing.Id, Catalog.Zelda.Id]);
    }

    [Fact]
    public async Task List_SortByMetacriticDesc_PutsMissingScoresLast()
    {
        var page = await ListAsync("?sortBy=metacritic&sortDir=desc");

        page.Items[^1].Id.ShouldBe(Catalog.Stardew.Id);
        page.Items.Take(2).Select(g => g.Metacritic).ShouldBe([(short)96, (short)96]);
    }

    [Fact]
    public async Task List_SortByReleaseDateDesc()
    {
        var page = await ListAsync("?sortBy=releaseDate&sortDir=desc&pageSize=2");

        page.Items.Select(g => g.Id).ShouldBe([Catalog.Nightreign.Id, Catalog.Zelda.Id]);
    }

    [Fact]
    public async Task List_Paging_ReturnsRequestedPage()
    {
        var page2 = await ListAsync("?pageSize=2&page=2");

        page2.TotalCount.ShouldBe(5);
        page2.TotalPages.ShouldBe(3);
        page2.Items.Select(g => g.Title).ShouldBe(["Hades", "Stardew Valley"]);
    }

    [Theory]
    [InlineData("?sortBy=rating", "sortBy")]
    [InlineData("?pageSize=51", "pageSize")]
    [InlineData("?page=0", "page")]
    public async Task List_InvalidParameter_Returns400WithErrors(string query, string expectedKey)
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/games{query}", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);
        problem.ShouldNotBeNull().Errors.Keys.ShouldContain(expectedKey);
        problem.Type.ShouldBe("https://gamex.rs/errors/validation");
    }

    [Fact]
    public async Task Get_ExistingGame_ReturnsDetail()
    {
        var game = await _client.GetFromJsonAsync<GameDetailDto>($"/api/v1/games/{Catalog.EldenRing.Id}", Ct);

        game.ShouldNotBeNull();
        game.Title.ShouldBe("Elden Ring");
        game.Description.ShouldBe("About Elden Ring");
        game.RawgSlug.ShouldBe("slug-326243");
        game.ReleaseDate.ShouldBe(new DateOnly(2022, 2, 25));
        game.Metacritic.ShouldBe((short)96);
        game.Platforms.Select(p => p.Name).ShouldBe(["PC", "PlayStation 5"]);
        game.Genres.Select(g => g.Name).ShouldBe(["Action", "RPG"]);
        game.Stats.OwnedCount.ShouldBe(142u);
    }

    [Fact]
    public async Task Get_UnknownGame_Returns404Problem()
    {
        var id = Guid.NewGuid();

        var response = await _client.GetAsync(new Uri($"/api/v1/games/{id}", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        problem.ShouldNotBeNull();
        problem.Type.ShouldBe("https://gamex.rs/errors/not-found");
        problem.Title.ShouldBe("Game not found");
        problem.Detail.ShouldBe($"No game with id {id} exists in the catalog");
    }

    [Fact]
    public async Task Search_PrefixMatchesComeFirst()
    {
        var results = await _client.GetFromJsonAsync<List<GameSummaryDto>>("/api/v1/games/search?q=of", Ct);

        // "Tears of the Kingdom" contains "of" but does not start with it; nothing starts with "of".
        results.ShouldNotBeNull().Select(g => g.Id).ShouldBe([Catalog.Zelda.Id]);

        var ring = await _client.GetFromJsonAsync<List<GameSummaryDto>>("/api/v1/games/search?q=ring", Ct);
        ring.ShouldNotBeNull().Select(g => g.Id).ShouldBe([Catalog.EldenRing.Id, Catalog.Nightreign.Id]);

        var el = await _client.GetFromJsonAsync<List<GameSummaryDto>>("/api/v1/games/search?q=el", Ct);
        el.ShouldNotBeNull()[0].Id.ShouldBe(Catalog.EldenRing.Id);
    }

    [Fact]
    public async Task Search_TooShortTerm_Returns400()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/games/search?q=e", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Platforms_ReturnsAllSortedByName()
    {
        var platforms = await _client.GetFromJsonAsync<List<PlatformDto>>("/api/v1/platforms", Ct);

        platforms.ShouldNotBeNull().Select(p => p.Slug).ShouldBe(["nintendo-switch", "pc", "playstation5"]);
    }

    [Fact]
    public async Task Genres_ReturnsAllSortedByName()
    {
        var genres = await _client.GetFromJsonAsync<List<GenreDto>>("/api/v1/genres", Ct);

        genres.ShouldNotBeNull().Select(g => g.Name).ShouldBe(["Action", "RPG"]);
    }
}

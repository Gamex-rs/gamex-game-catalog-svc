using Gamex.GameCatalog.Application.Games;
using SortDirection = Gamex.GameCatalog.Application.Games.SortDirection;

namespace Gamex.GameCatalog.UnitTests.Application;

public sealed class GameListQueryTests
{
    [Fact]
    public void Create_WithNoParameters_UsesDefaults()
    {
        var (query, errors) = GameListQuery.Create(null, null, null, null, null, null, null);

        errors.ShouldBeEmpty();
        query.ShouldNotBeNull();
        query.Q.ShouldBeNull();
        query.PlatformIds.ShouldBeEmpty();
        query.SortBy.ShouldBe(GameSortBy.Title);
        query.SortDirection.ShouldBe(SortDirection.Asc);
        query.Page.ShouldBe(1);
        query.PageSize.ShouldBe(GameListQuery.DefaultPageSize);
    }

    [Theory]
    [InlineData("ownedCount", "desc", GameSortBy.OwnedCount, SortDirection.Desc)]
    [InlineData("RELEASEDATE", "ASC", GameSortBy.ReleaseDate, SortDirection.Asc)]
    [InlineData("metacritic", null, GameSortBy.Metacritic, SortDirection.Asc)]
    public void Create_ParsesSortCaseInsensitively(string sortBy, string? sortDir, GameSortBy expectedSort, SortDirection expectedDir)
    {
        var (query, _) = GameListQuery.Create(null, null, null, sortBy, sortDir, null, null);

        query.ShouldNotBeNull();
        query.SortBy.ShouldBe(expectedSort);
        query.SortDirection.ShouldBe(expectedDir);
    }

    [Fact]
    public void Create_TrimsSearchAndDeduplicatesPlatforms()
    {
        var ps5 = Guid.NewGuid();

        var (query, _) = GameListQuery.Create("  elden ", [ps5, ps5], null, null, null, null, null);

        query.ShouldNotBeNull();
        query.Q.ShouldBe("elden");
        query.PlatformIds.ShouldBe([ps5]);
    }

    [Fact]
    public void Create_WhitespaceSearch_IsIgnored()
    {
        var (query, _) = GameListQuery.Create("   ", null, null, null, null, null, null);

        query.ShouldNotBeNull();
        query.Q.ShouldBeNull();
    }

    [Theory]
    [InlineData("rating", null, null, null, "sortBy")]
    [InlineData(null, "up", null, null, "sortDir")]
    [InlineData(null, null, 0, null, "page")]
    [InlineData(null, null, null, 0, "pageSize")]
    [InlineData(null, null, null, 51, "pageSize")]
    public void Create_InvalidValue_ReturnsError(string? sortBy, string? sortDir, int? page, int? pageSize, string expectedKey)
    {
        var (query, errors) = GameListQuery.Create(null, null, null, sortBy, sortDir, page, pageSize);

        query.ShouldBeNull();
        errors.Keys.ShouldBe([expectedKey]);
    }

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    public void PagedResult_TotalPages(int total, int pageSize, int expectedPages)
    {
        new PagedResult<int>([], 1, pageSize, total).TotalPages.ShouldBe(expectedPages);
    }
}

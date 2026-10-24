using Gamex.GameCatalog.Infrastructure.Persistence;

namespace Gamex.GameCatalog.UnitTests.Persistence;

public sealed class SnakeCaseNamingTests
{
    [Theory]
    [InlineData("Id", "id")]
    [InlineData("GameId", "game_id")]
    [InlineData("CoverImageUrl", "cover_image_url")]
    [InlineData("ForExchangeCount", "for_exchange_count")]
    [InlineData("RawgId", "rawg_id")]
    [InlineData("HTMLParser", "html_parser")]
    public void ToSnakeCase_ConvertsPascalCase(string input, string expected)
    {
        SnakeCaseNaming.ToSnakeCase(input).ShouldBe(expected);
    }
}

using Gamex.GameCatalog.Infrastructure.Persistence.Queries;

namespace Gamex.GameCatalog.UnitTests.Persistence;

public sealed class GameQueriesTests
{
    [Theory]
    [InlineData("zelda", "zelda")]
    [InlineData("100%", @"100\%")]
    [InlineData("a_b", @"a\_b")]
    [InlineData(@"c:\", @"c:\\")]
    public void EscapeLike_EscapesWildcards(string input, string expected)
    {
        GameQueries.EscapeLike(input).ShouldBe(expected);
    }
}

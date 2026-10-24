using System.Net;
using Gamex.GameCatalog.IntegrationTests.Infrastructure;

namespace Gamex.GameCatalog.IntegrationTests;

[Collection(CatalogApiFixture.Name)]
public sealed class HealthEndpointTests(CatalogApiFactory factory)
{
    [Fact]
    public async Task Health_WithMigratedDatabase_ReturnsOk()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

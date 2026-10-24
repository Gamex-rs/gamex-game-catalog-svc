using Gamex.GameCatalog.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Gamex.GameCatalog.IntegrationTests.Infrastructure;

/// <summary>API with a read-only <see cref="TestCatalog"/> dataset; RAWG and S3 are disabled.</summary>
public sealed class CatalogApiFactory : ApiFactoryBase
{
    public TestCatalog Catalog { get; } = new();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        // Building the host runs migrations (DB_MIGRATE_ON_STARTUP=true).
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await Catalog.SeedAsync(db, CancellationToken.None);
    }
}

[CollectionDefinition(Name)]
public sealed class CatalogApiFixture : ICollectionFixture<CatalogApiFactory>
{
    public const string Name = "CatalogApi";
}

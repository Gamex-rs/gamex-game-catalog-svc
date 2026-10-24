using Gamex.GameCatalog.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MySql;

namespace Gamex.GameCatalog.IntegrationTests.Infrastructure;

/// <summary>Runs the API against a throwaway MySQL container seeded with <see cref="TestCatalog"/>. Requires Docker.</summary>
public sealed class CatalogApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MySqlContainer _mySql = new MySqlBuilder("mysql:8.0")
        .WithDatabase("games_db")
        .Build();

    public TestCatalog Catalog { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _mySql.StartAsync();

        // Building the host runs migrations (DB_MIGRATE_ON_STARTUP=true).
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await Catalog.SeedAsync(db, CancellationToken.None);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DB_CONNECTION_STRING", _mySql.GetConnectionString());
        builder.UseSetting("DB_MIGRATE_ON_STARTUP", "true");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _mySql.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class CatalogApiFixture : ICollectionFixture<CatalogApiFactory>
{
    public const string Name = "CatalogApi";
}

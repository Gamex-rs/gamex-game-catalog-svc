using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gamex.GameCatalog.Infrastructure.Persistence;

/// <summary>Used by <c>dotnet ef</c>; creating migrations does not connect to the database.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
            ?? "Server=localhost;Port=3307;Database=games_db;User=root;Password=localdev;";

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseMySQL(connectionString)
            .Options;

        return new CatalogDbContext(options);
    }
}

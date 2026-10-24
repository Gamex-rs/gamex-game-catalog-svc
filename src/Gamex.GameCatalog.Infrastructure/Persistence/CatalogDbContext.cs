using Gamex.GameCatalog.Domain.Games;
using Microsoft.EntityFrameworkCore;

namespace Gamex.GameCatalog.Infrastructure.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<GameStats> GameStats => Set<GameStats>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
        modelBuilder.ApplySnakeCaseNames();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Guid>().HaveColumnType("char(36)");
        configurationBuilder.Properties<DateTime>()
            .HaveColumnType("datetime(6)")
            .HaveConversion<UtcDateTimeConverter>();
    }
}

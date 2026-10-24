using Gamex.GameCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gamex.GameCatalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["DB_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("DB_CONNECTION_STRING is not configured.");

        services.AddDbContext<CatalogDbContext>(options => options.UseMySQL(connectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddHealthChecks().AddDbContextCheck<CatalogDbContext>("database");

        return services;
    }
}

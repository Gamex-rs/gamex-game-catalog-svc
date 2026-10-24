using Gamex.GameCatalog.Application.Games;
using Microsoft.Extensions.DependencyInjection;

namespace Gamex.GameCatalog.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IGameImporter, GameImporter>();
        services.AddScoped<IGameSearchService, GameSearchService>();
        services.AddScoped<ICoverImageAttacher, CoverImageAttacher>();
        services.AddScoped<IGameAdminService, GameAdminService>();
        services.AddScoped<ICatalogReplayService, CatalogReplayService>();
        return services;
    }
}

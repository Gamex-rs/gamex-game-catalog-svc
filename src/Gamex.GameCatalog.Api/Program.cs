using Gamex.GameCatalog.Api.Endpoints;
using Gamex.GameCatalog.Infrastructure;
using Gamex.GameCatalog.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapGameEndpoints();

if (app.Configuration.GetValue("DB_MIGRATE_ON_STARTUP", app.Environment.IsDevelopment()))
{
    await app.Services.MigrateDatabaseAsync();
}

await app.RunAsync();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;

using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.MySql;

namespace Gamex.GameCatalog.IntegrationTests.Infrastructure;

/// <summary>Runs the API against its own throwaway MySQL container. Requires Docker.</summary>
public abstract class ApiFactoryBase : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Stands in for the User Library Service's signing key.</summary>
    private static readonly RSA SigningKey = RSA.Create(2048);

    private readonly MySqlContainer _mySql = new MySqlBuilder("mysql:8.0")
        .WithDatabase("games_db")
        .Build();

    public virtual async ValueTask InitializeAsync() => await _mySql.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DB_CONNECTION_STRING", _mySql.GetConnectionString());
        builder.UseSetting("DB_MIGRATE_ON_STARTUP", "true");
        builder.UseSetting("JWT_PUBLIC_KEY", SigningKey.ExportSubjectPublicKeyInfoPem());
        builder.UseSetting("RAWG_API_KEY", "");
        builder.UseSetting("S3_MEDIA_BUCKET", "");
        builder.UseSetting("NIGHTLY_REFRESH_INTERVAL_MS", "0");
    }

    /// <summary>Signs a token like the User Library Service does (RS256, claims sub/email/name/role).</summary>
    public static string CreateToken(string role, RSA? key = null, DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("email", "admin@gamex.rs"),
                new Claim("name", "Test User"),
                new Claim("role", role),
            ]),
            NotBefore = (expires ?? now.AddMinutes(15)).AddMinutes(-30),
            IssuedAt = (expires ?? now.AddMinutes(15)).AddMinutes(-30),
            Expires = expires ?? now.AddMinutes(15),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(key ?? SigningKey), SecurityAlgorithms.RsaSha256),
        });
    }

    public HttpClient CreateClient(string role)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(role));
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _mySql.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

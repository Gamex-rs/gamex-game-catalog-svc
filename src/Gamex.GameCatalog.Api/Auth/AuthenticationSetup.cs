using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Gamex.GameCatalog.Api.Auth;

internal static partial class AuthenticationSetup
{
    public const string AdminPolicy = "Admin";
    public const string RoleClaim = "role";
    public const string AdminRole = "admin";

    /// <summary>
    /// Validates RS256 JWTs issued by the User Library Service using its public key (<c>JWT_PUBLIC_KEY</c>, PEM).
    /// Issuer and audience are validated only when <c>JWT_ISSUER</c> / <c>JWT_AUDIENCE</c> are set.
    /// </summary>
    public static IServiceCollection AddGamexAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var signingKey = LoadPublicKey(configuration["JWT_PUBLIC_KEY"]);
        if (signingKey is null && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("JWT_PUBLIC_KEY is not configured.");
        }

        var issuer = configuration["JWT_ISSUER"];
        var audience = configuration["JWT_AUDIENCE"];

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep claim names as issued ("role", "sub") instead of mapping to WS-* URIs.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Without a key (Development only) every token fails validation, so admin endpoints return 401.
                    IssuerSigningKey = signingKey,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidateIssuer = !string.IsNullOrEmpty(issuer),
                    ValidIssuer = issuer,
                    ValidateAudience = !string.IsNullOrEmpty(audience),
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = RoleClaim,
                    NameClaimType = "name",
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy.RequireAuthenticatedUser().RequireRole(AdminRole));

        return services;
    }

    private static RsaSecurityKey? LoadPublicKey(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return null;
        }

        // Environment variables often carry the PEM with escaped newlines.
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem.Replace("\\n", "\n", StringComparison.Ordinal));
        return new RsaSecurityKey(rsa);
    }
}

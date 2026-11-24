using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

// ADR-0002: shared passwords trade for signed role tokens; no identity provider.

public static class Roles
{
    public const string Guest = "guest";
    public const string Admin = "admin";
}

public static class Policies
{
    public const string Guest = "Guest"; // guest or admin role
    public const string Admin = "Admin"; // admin role only
    public const string AuthRateLimit = "auth";
}

public record AuthRequest(string? Password);
public record AuthResponse(string Role, string Token);

public sealed class AuthSettings
{
    public const string Issuer = "wedding-api";
    public static readonly TimeSpan GuestLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan AdminLifetime = TimeSpan.FromHours(24);

    public required string EventPassword { get; init; }
    public required string AdminPassword { get; init; }
    public required string TokenSigningKey { get; init; }
    public int AuthRequestsPerMinute { get; init; } = 5;

    public static AuthSettings From(IConfiguration config)
    {
        string Require(string key) => config[key] is { Length: > 0 } v
            ? v
            : throw new InvalidOperationException($"Configuration '{key}' is required.");

        var settings = new AuthSettings
        {
            EventPassword = Require("EventPassword"),
            AdminPassword = Require("AdminPassword"),
            TokenSigningKey = Require("TokenSigningKey"),
            AuthRequestsPerMinute = config.GetValue("AuthRequestsPerMinute", 5),
        };
        if (Encoding.UTF8.GetByteCount(settings.TokenSigningKey) < 32)
            throw new InvalidOperationException("Configuration 'TokenSigningKey' must be at least 32 bytes.");
        return settings;
    }

    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(TokenSigningKey));
}

public sealed class TokenService(AuthSettings settings, TimeProvider clock)
{
    /// <summary>Returns the role the password grants, or null. Both secrets are always compared.</summary>
    public string? RoleFor(string? password)
    {
        var candidate = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? ""));
        var isGuest = Matches(candidate, settings.EventPassword);
        var isAdmin = Matches(candidate, settings.AdminPassword);
        return isAdmin ? Roles.Admin : isGuest ? Roles.Guest : null;
    }

    // Hashing first makes the comparison length-independent as well as constant-time
    static bool Matches(byte[] candidateHash, string secret) =>
        CryptographicOperations.FixedTimeEquals(candidateHash, SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public string Issue(string role)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var lifetime = role == Roles.Admin ? AuthSettings.AdminLifetime : AuthSettings.GuestLifetime;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = AuthSettings.Issuer,
            Audience = AuthSettings.Issuer,
            Subject = new ClaimsIdentity([new Claim("role", role), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))]),
            IssuedAt = now,
            NotBefore = now,
            Expires = now + lifetime,
            SigningCredentials = new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }
}

public static class AuthExtensions
{
    public static void AddSharedPasswordAuth(this WebApplicationBuilder builder)
    {
        var settings = AuthSettings.From(builder.Configuration);
        var services = builder.Services;
        services.AddSingleton(settings);
        services.AddSingleton<TokenService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                IssuerSigningKey = settings.SigningKey,
                ValidIssuer = AuthSettings.Issuer,
                ValidAudience = AuthSettings.Issuer,
                RoleClaimType = "role",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            };
        });
        // A revoked token id (logout) is rejected even before the token expires
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<Database>((o, db) =>
                o.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async ctx =>
                    {
                        var jti = ctx.Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);
                        if (jti is null || await Revocations.IsRevoked(db, jti))
                            ctx.Fail("Token has been revoked.");
                    },
                });
        // Judge expiry by the injected clock so tests can move time
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TimeProvider>((o, clock) =>
                o.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, _) =>
                {
                    var now = clock.GetUtcNow().UtcDateTime;
                    return expires is not null && now < expires && (notBefore is null || now >= notBefore);
                });

        var guestPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireRole(Roles.Guest, Roles.Admin).Build();
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Guest, guestPolicy)
            .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin))
            .SetFallbackPolicy(guestPolicy); // everything is closed unless marked AllowAnonymous

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(Policies.AuthRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.AuthRequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

        // Behind the Container Apps ingress the client IP is in X-Forwarded-For
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });
    }

    public static void MapAuth(this WebApplication app)
    {
        app.MapPost("/auth", (AuthRequest req, TokenService tokens) =>
            tokens.RoleFor(req.Password) is { } role
                ? Results.Ok(new AuthResponse(role, tokens.Issue(role)))
                : Results.Unauthorized())
            .AllowAnonymous()
            .RequireRateLimiting(Policies.AuthRateLimit);

        // Any authenticated role; the token is already known valid, so it can't be a repeat
        app.MapPost("/auth/logout", async (ClaimsPrincipal user, Database db, TimeProvider clock) =>
        {
            var jti = user.FindFirstValue(JwtRegisteredClaimNames.Jti);
            var exp = long.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Exp), out var e) ? e : 0;
            if (jti is null || exp == 0) return Results.Unauthorized();
            await Revocations.Revoke(db, jti, exp, clock);
            return Results.NoContent();
        });
    }
}

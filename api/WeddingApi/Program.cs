using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

if (builder.Environment.IsDevelopment())
{
    // ADR-0003: Development may only talk to the dev Cosmos account or the local emulator
    var connection = config["CosmosConnection"];
    if (string.IsNullOrWhiteSpace(connection))
        throw new InvalidOperationException("CosmosConnection is missing. Set it with `dotnet user-secrets set CosmosConnection ...` (see README).");

    var endpoint = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connection }
        .TryGetValue("AccountEndpoint", out var e) ? e?.ToString() : null;
    var host = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host : null;
    var devHost = config["DevCosmosHost"];
    var allowed = host is "localhost" or "127.0.0.1"
        || (host is not null && string.Equals(host, devHost, StringComparison.OrdinalIgnoreCase));
    if (!allowed)
        throw new InvalidOperationException(
            $"Development must use the dev Cosmos account (DevCosmosHost) or the local emulator, but CosmosConnection points at {host ?? "an unparseable endpoint"}.");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => new CosmosClient(
    config["CosmosConnection"],
    new CosmosClientOptions
    {
        SerializerOptions = new() { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase },
        // The Docker emulator is only reachable through its gateway endpoint
        ConnectionMode = config.GetValue<bool>("CosmosGatewayMode") ? ConnectionMode.Gateway : ConnectionMode.Direct,
    }));

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(config.GetSection("AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.AddSharedPasswordAuth();

var app = builder.Build();
app.UseForwardedHeaders();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok()).AllowAnonymous();
app.MapAuth();

app.MapGuestAdmin();
app.MapSettingsAdmin();
app.MapPartyRsvp();
app.MapAdminRsvp();
app.MapGuestEdit();
app.MapLookup();

app.Run();

public static class Partitioning
{
    public const string Path = "/pk";
    public const string Value = "wedding";
}

// Lets the test project host the API in-process
public partial class Program;

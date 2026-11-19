using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

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
app.MapPartyRsvp();

app.Run();

public static class Partitioning
{
    public const string Path = "/pk";
    public const string Value = "wedding";
}

// Lets the test project host the API in-process
public partial class Program;

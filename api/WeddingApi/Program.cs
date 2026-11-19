using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Identity.Web;

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

// Admin auth: Entra ID tokens + an email allowlist
builder.Services.AddMicrosoftIdentityWebApiAuthentication(config, "AzureAd");
builder.Services.AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireAssertion(ctx =>
{
    var email = ctx.User.FindFirst("preferred_username")?.Value;
    var admins = config.GetSection("AdminEmails").Get<string[]>() ?? [];
    return email is not null && admins.Contains(email, StringComparer.OrdinalIgnoreCase);
})));

var app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

Container Parties(CosmosClient c) =>
    c.GetContainer(config["CosmosDatabase"] ?? "wedding", config["PartiesContainer"] ?? "parties");

// ADR-0001: every document shares one partition key value
var pk = new PartitionKey(Partitioning.Value);

app.MapGet("/health", () => Results.Ok());

app.MapGet("/party/{code}", async (string code, CosmosClient cosmos) =>
{
    var id = code.Trim().ToUpperInvariant();
    try
    {
        var res = await Parties(cosmos).ReadItemAsync<Party>(id, pk);
        return Results.Ok(res.Resource);
    }
    catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
    {
        return Results.NotFound();
    }
});

app.MapPost("/rsvp/{code}", async (string code, RsvpRequest req, CosmosClient cosmos, TimeProvider clock) =>
{
    var id = code.Trim().ToUpperInvariant();
    var container = Parties(cosmos);

    string[] allowed = ["yes", "no"];
    if (req.Guests is null || req.Guests.Any(g => !allowed.Contains(g.Rsvp)))
        return Results.BadRequest("Each guest must answer 'yes' or 'no'.");

    try
    {
        var res = await container.ReadItemAsync<Party>(id, pk);
        var party = res.Resource;

        // Only known guests can be updated; names come from the stored party, never the client.
        var guests = party.Guests.Select(existing =>
        {
            var answer = req.Guests.FirstOrDefault(a =>
                string.Equals(a.Name, existing.Name, StringComparison.OrdinalIgnoreCase));
            return answer is null
                ? existing
                : existing with { Rsvp = answer.Rsvp, Meal = answer.Rsvp == "yes" ? answer.Meal : null };
        }).ToList();

        var updated = party with
        {
            Email = string.IsNullOrWhiteSpace(req.Email) ? party.Email : req.Email.Trim(),
            Guests = guests,
            RespondedAt = clock.GetUtcNow().UtcDateTime,
        };

        await container.ReplaceItemAsync(updated, id, pk,
            new ItemRequestOptions { IfMatchEtag = res.ETag });
        return Results.NoContent();
    }
    catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
    {
        return Results.NotFound();
    }
    catch (CosmosException e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
    {
        return Results.Conflict("This RSVP was just changed elsewhere. Please reload and try again.");
    }
});

app.MapGet("/admin/parties", async (CosmosClient cosmos) =>
{
    var it = Parties(cosmos).GetItemQueryIterator<Party>("SELECT * FROM c", requestOptions: new QueryRequestOptions { PartitionKey = pk });
    var all = new List<Party>();
    while (it.HasMoreResults) all.AddRange(await it.ReadNextAsync());
    return Results.Ok(all);
}).RequireAuthorization("Admin");

app.Run();

public record Guest(string Name, string Rsvp, string? Meal, bool IsPlusOne);
public record Party(string Id, string PartyName, string? Email, List<Guest> Guests, DateTime? RespondedAt)
{
    public string Pk { get; init; } = Partitioning.Value;
}
public record GuestAnswer(string Name, string Rsvp, string? Meal);
public record RsvpRequest(string? Email, List<GuestAnswer> Guests);

public static class Partitioning
{
    public const string Path = "/pk";
    public const string Value = "wedding";
}

// Lets the test project host the API in-process
public partial class Program;

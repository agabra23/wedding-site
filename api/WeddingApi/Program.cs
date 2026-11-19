using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddSingleton(_ => new CosmosClient(
    config["CosmosConnection"],
    new CosmosClientOptions
    {
        SerializerOptions = new() { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase }
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
    c.GetContainer("wedding", config["PartiesContainer"] ?? "parties");

app.MapGet("/health", () => Results.Ok());

app.MapGet("/party/{code}", async (string code, CosmosClient cosmos) =>
{
    var id = code.Trim().ToUpperInvariant();
    try
    {
        var res = await Parties(cosmos).ReadItemAsync<Party>(id, new PartitionKey(id));
        return Results.Ok(res.Resource);
    }
    catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
    {
        return Results.NotFound();
    }
});

app.MapPost("/rsvp/{code}", async (string code, RsvpRequest req, CosmosClient cosmos) =>
{
    var id = code.Trim().ToUpperInvariant();
    var container = Parties(cosmos);

    string[] allowed = ["yes", "no"];
    if (req.Guests is null || req.Guests.Any(g => !allowed.Contains(g.Rsvp)))
        return Results.BadRequest("Each guest must answer 'yes' or 'no'.");

    try
    {
        var res = await container.ReadItemAsync<Party>(id, new PartitionKey(id));
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
            RespondedAt = DateTime.UtcNow,
        };

        await container.ReplaceItemAsync(updated, id, new PartitionKey(id),
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
    var it = Parties(cosmos).GetItemQueryIterator<Party>("SELECT * FROM c");
    var all = new List<Party>();
    while (it.HasMoreResults) all.AddRange(await it.ReadNextAsync());
    return Results.Ok(all);
}).RequireAuthorization("Admin");

app.Run();

public record Guest(string Name, string Rsvp, string? Meal, bool IsPlusOne);
public record Party(string Id, string PartyName, string? Email, List<Guest> Guests, DateTime? RespondedAt);
public record GuestAnswer(string Name, string Rsvp, string? Meal);
public record RsvpRequest(string? Email, List<GuestAnswer> Guests);

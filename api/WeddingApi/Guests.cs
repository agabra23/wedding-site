using System.Net;
using Microsoft.Azure.Cosmos;

public record GuestName(string? FirstName, string? LastName);
public record AddGuestsRequest(string? PartyId, List<GuestName>? Guests);

public record GuestDto(string Id, string FirstName, string LastName, string Rsvp, DateTime? RsvpChangedAt, string? RsvpChangedBy);
public record PartyDto(string Id, List<GuestDto> Guests)
{
    public static PartyDto From(Party p) => new(p.Id,
        p.Guests.Select(g => new GuestDto(g.Id, g.FirstName, g.LastName, g.Rsvp, g.RsvpChangedAt, g.RsvpChangedBy)).ToList());
}

public static class GuestAdmin
{
    public static void MapGuestAdmin(this WebApplication app)
    {
        // ADR-0001: every document shares one partition key value
        var pk = new PartitionKey(Partitioning.Value);

        Container Parties(CosmosClient c) =>
            c.GetContainer(app.Configuration["CosmosDatabase"] ?? "wedding", app.Configuration["PartiesContainer"] ?? "parties");

        async Task<List<Party>> AllParties(Container container)
        {
            var it = container.GetItemQueryIterator<Party>("SELECT * FROM c WHERE IS_DEFINED(c.guests)",
                requestOptions: new QueryRequestOptions { PartitionKey = pk });
            var all = new List<Party>();
            while (it.HasMoreResults) all.AddRange(await it.ReadNextAsync());
            return all;
        }

        app.MapGet("/admin/parties", async (CosmosClient cosmos) =>
            Results.Ok((await AllParties(Parties(cosmos))).Select(PartyDto.From)))
            .RequireAuthorization(Policies.Admin);

        app.MapPost("/admin/guests", async (AddGuestsRequest req, CosmosClient cosmos) =>
        {
            if (req.Guests is not { Count: > 0 } names)
                return Results.BadRequest("Every guest needs a first and last name.");

            var container = Parties(cosmos);
            Party? existing = null;
            string? etag = null;
            if (!string.IsNullOrWhiteSpace(req.PartyId))
            {
                try
                {
                    var read = await container.ReadItemAsync<Party>(req.PartyId, pk);
                    existing = read.Resource;
                    etag = read.ETag;
                }
                catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    return Results.NotFound();
                }
            }

            var result = new GuestList(await AllParties(container)).Add(names, existing);
            if (result.Refusal is { } refusal) return refusal.ToHttp();
            var saved = result.Value.Party;

            try
            {
                if (result.Value is PartyChange.Created)
                    await container.CreateItemAsync(saved, pk);
                else
                    await container.ReplaceItemAsync(saved, saved.Id, pk, new ItemRequestOptions { IfMatchEtag = etag });
                return Results.Created($"/admin/parties/{saved.Id}", PartyDto.From(saved));
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return Results.Conflict(RefusalResults.ChangedElsewhere);
            }
        }).RequireAuthorization(Policies.Admin);
    }
}

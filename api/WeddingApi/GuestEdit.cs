using System.Net;
using Microsoft.Azure.Cosmos;

public record RenameGuestRequest(string? FirstName, string? LastName);

public static class GuestEdit
{
    public static void MapGuestEdit(this WebApplication app)
    {
        var pk = new PartitionKey(Partitioning.Value);

        async Task<List<Party>> AllParties(Container container)
        {
            var it = container.GetItemQueryIterator<Party>("SELECT * FROM c WHERE IS_DEFINED(c.guests)",
                requestOptions: new QueryRequestOptions { PartitionKey = pk });
            var all = new List<Party>();
            while (it.HasMoreResults) all.AddRange(await it.ReadNextAsync());
            return all;
        }

        // Reads the Party holding the Guest together with its etag, so writes can't clobber concurrent changes
        async Task<(Party? Party, string? ETag)> FindParty(Container container, string guestId)
        {
            var query = new QueryDefinition(
                "SELECT VALUE c.id FROM c WHERE EXISTS(SELECT VALUE g FROM g IN c.guests WHERE g.id = @id)")
                .WithParameter("@id", guestId);
            var it = container.GetItemQueryIterator<string>(query, requestOptions: new QueryRequestOptions { PartitionKey = pk });
            string? partyId = null;
            while (partyId is null && it.HasMoreResults)
                partyId = (await it.ReadNextAsync()).FirstOrDefault();
            if (partyId is null) return (null, null);
            var read = await container.ReadItemAsync<Party>(partyId, pk);
            return (read.Resource, read.ETag);
        }

        app.MapPatch("/admin/guests/{guestId}", async (string guestId, RenameGuestRequest req, CosmosClient cosmos) =>
        {
            if ((req.FirstName is null && req.LastName is null)
                || (req.FirstName is not null && string.IsNullOrWhiteSpace(req.FirstName))
                || (req.LastName is not null && string.IsNullOrWhiteSpace(req.LastName)))
                return Results.BadRequest("Names can't be blank.");

            var container = cosmos.Container(app.Configuration);
            var (party, etag) = await FindParty(container, guestId);
            if (party is null) return Results.NotFound();

            var current = party.Guests.First(g => g.Id == guestId);
            var renamed = current with
            {
                FirstName = req.FirstName?.Trim() ?? current.FirstName,
                LastName = req.LastName?.Trim() ?? current.LastName,
            };
            renamed = renamed with
            {
                FirstNameNormalized = Names.Normalize(renamed.FirstName),
                LastNameNormalized = Names.Normalize(renamed.LastName),
            };

            var clash = (await AllParties(container)).SelectMany(p => p.Guests)
                .Any(g => g.Id != guestId
                    && g.FirstNameNormalized == renamed.FirstNameNormalized
                    && g.LastNameNormalized == renamed.LastNameNormalized);
            if (clash)
                return Results.Conflict($"A guest named {renamed.FirstName} {renamed.LastName} already exists.");

            var updated = party with { Guests = party.Guests.Select(g => g.Id == guestId ? renamed : g).ToList() };
            try
            {
                await container.ReplaceItemAsync(updated, updated.Id, pk, new ItemRequestOptions { IfMatchEtag = etag });
                return Results.Ok(PartyDto.From(updated));
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return Results.Conflict("This party was just changed elsewhere. Please try again.");
            }
        }).RequireAuthorization(Policies.Admin);

        app.MapDelete("/admin/guests/{guestId}", async (string guestId, CosmosClient cosmos) =>
        {
            var container = cosmos.Container(app.Configuration);
            var (party, etag) = await FindParty(container, guestId);
            if (party is null) return Results.NotFound();

            var remaining = party.Guests.Where(g => g.Id != guestId).ToList();
            var options = new ItemRequestOptions { IfMatchEtag = etag };
            try
            {
                if (remaining.Count == 0)
                    await container.DeleteItemAsync<Party>(party.Id, pk, options);
                else
                    await container.ReplaceItemAsync(party with { Guests = remaining }, party.Id, pk, options);
                return Results.NoContent();
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return Results.Conflict("This party was just changed elsewhere. Please try again.");
            }
        }).RequireAuthorization(Policies.Admin);
    }
}

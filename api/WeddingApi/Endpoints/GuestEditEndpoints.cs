using System.Net;
using Microsoft.Azure.Cosmos;

public record RenameGuestRequest(string? FirstName, string? LastName);
public record MoveGuestRequest(string? PartyId);

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
            if (req.FirstName is null && req.LastName is null)
                return Results.BadRequest("Names can't be blank.");

            var container = cosmos.Container(app.Configuration);
            var (party, etag) = await FindParty(container, guestId);
            if (party is null) return Results.NotFound();

            var result = new GuestList(await AllParties(container)).Rename(party, guestId, req.FirstName, req.LastName);
            if (result.Refusal is { } refusal) return refusal.ToHttp();

            var updated = result.Value;
            try
            {
                await container.ReplaceItemAsync(updated, updated.Id, pk, new ItemRequestOptions { IfMatchEtag = etag });
                return Results.Ok(PartyDto.From(updated));
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return Results.Conflict(RefusalResults.ChangedElsewhere);
            }
        }).RequireAuthorization(Policies.Admin);

        app.MapDelete("/admin/guests/{guestId}", async (string guestId, CosmosClient cosmos) =>
        {
            var container = cosmos.Container(app.Configuration);
            var (party, etag) = await FindParty(container, guestId);
            if (party is null) return Results.NotFound();

            var result = party.Remove(guestId);
            if (result.Refusal is { } refusal) return refusal.ToHttp();

            var options = new ItemRequestOptions { IfMatchEtag = etag };
            try
            {
                if (result.Value is PartyChange.Gone)
                    await container.DeleteItemAsync<Party>(party.Id, pk, options);
                else
                    await container.ReplaceItemAsync(result.Value.Party, party.Id, pk, options);
                return Results.NoContent();
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return Results.Conflict(RefusalResults.ChangedElsewhere);
            }
        }).RequireAuthorization(Policies.Admin);

        app.MapPost("/admin/guests/{guestId}/move", async (string guestId, MoveGuestRequest? req, CosmosClient cosmos) =>
        {
            var container = cosmos.Container(app.Configuration);
            var (source, sourceEtag) = await FindParty(container, guestId);
            if (source is null) return Results.NotFound();

            var destId = string.IsNullOrWhiteSpace(req?.PartyId) ? null : req.PartyId;

            Party? dest = destId == source.Id ? source : null;
            string? destEtag = null;
            if (destId is not null && dest is null)
            {
                try
                {
                    var read = await container.ReadItemAsync<Party>(destId, pk);
                    dest = read.Resource;
                    destEtag = read.ETag;
                }
                catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    return Results.NotFound();
                }
            }

            var result = source.Move(guestId, dest);
            if (result.Refusal is { } refusal) return refusal.ToHttp();
            var (from, to) = result.Value;
            if (to is PartyChange.Unchanged) return Results.Ok(PartyDto.From(to.Party));

            // ADR-0001: one transactional batch, so the Guest is never in two Parties or none
            var batch = container.CreateTransactionalBatch(pk);
            var sourceOptions = new TransactionalBatchItemRequestOptions { IfMatchEtag = sourceEtag };
            if (from is PartyChange.Gone)
                batch.DeleteItem(source.Id, sourceOptions);
            else
                batch.ReplaceItem(source.Id, from.Party, sourceOptions);
            if (to is PartyChange.Created)
                batch.CreateItem(to.Party);
            else
                batch.ReplaceItem(to.Party.Id, to.Party, new TransactionalBatchItemRequestOptions { IfMatchEtag = destEtag });

            using var response = await batch.ExecuteAsync();
            if (response.IsSuccessStatusCode) return Results.Ok(PartyDto.From(to.Party));
            if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict
                || Enumerable.Range(0, response.Count).Any(i => response[i].StatusCode == HttpStatusCode.PreconditionFailed))
                return Results.Conflict(RefusalResults.ChangedElsewhere);
            return Results.Problem($"Move failed ({(int)response.StatusCode}).");
        }).RequireAuthorization(Policies.Admin);
    }
}

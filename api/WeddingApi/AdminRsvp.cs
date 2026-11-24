using System.Net;
using Microsoft.Azure.Cosmos;

public record AdminRsvpRequest(string? Rsvp);

public static class AdminRsvp
{
    public static void MapAdminRsvp(this WebApplication app)
    {
        var pk = new PartitionKey(Partitioning.Value);

        app.MapPut("/admin/guests/{guestId}/rsvp", async (string guestId, AdminRsvpRequest req, CosmosClient cosmos, TimeProvider clock) =>
        {
            var container = cosmos.Container(app.Configuration);
            var query = new QueryDefinition(
                "SELECT VALUE c.id FROM c WHERE EXISTS(SELECT VALUE g FROM g IN c.guests WHERE g.id = @id)")
                .WithParameter("@id", guestId);
            var it = container.GetItemQueryIterator<string>(query, requestOptions: new QueryRequestOptions { PartitionKey = pk });

            string? partyId = null;
            while (partyId is null && it.HasMoreResults)
                partyId = (await it.ReadNextAsync()).FirstOrDefault();
            if (partyId is null) return Results.NotFound();

            // Read the whole Party with its etag so the replace can't clobber a concurrent change
            var read = await container.ReadItemAsync<Party>(partyId, pk);
            var etag = read.ETag;

            var result = read.Resource.SetRsvpAsAdmin(guestId, req.Rsvp, clock.GetUtcNow().UtcDateTime);
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
    }
}

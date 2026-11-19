using System.Net;
using Microsoft.Azure.Cosmos;

public record AdminRsvpRequest(string? Rsvp);

public static class AdminRsvp
{
    static readonly string[] Allowed = [Rsvps.Pending, "attending", "declined"];

    public static void MapAdminRsvp(this WebApplication app)
    {
        var pk = new PartitionKey(Partitioning.Value);

        // Ignores the RSVP Deadline on purpose: admins can always correct an answer
        app.MapPut("/admin/guests/{guestId}/rsvp", async (string guestId, AdminRsvpRequest req, CosmosClient cosmos, TimeProvider clock) =>
        {
            if (!Allowed.Contains(req.Rsvp))
                return Results.BadRequest("RSVP must be pending, attending or declined.");

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
            var party = read.Resource;
            var etag = read.ETag;

            var now = clock.GetUtcNow().UtcDateTime;
            var updated = party with
            {
                Guests = party.Guests.Select(g => g.Id == guestId
                    ? g with { Rsvp = req.Rsvp!, RsvpChangedAt = now, RsvpChangedBy = "admin" }
                    : g).ToList()
            };

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
    }
}

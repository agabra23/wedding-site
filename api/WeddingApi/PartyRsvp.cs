using System.Net;
using Microsoft.Azure.Cosmos;

public record RsvpAnswer(string? GuestId, string? Rsvp);
public record RsvpRequest(List<RsvpAnswer>? Answers);

public static class PartyRsvp
{
    static readonly string[] Allowed = ["attending", "declined"];

    public static void MapPartyRsvp(this WebApplication app)
    {
        var pk = new PartitionKey(Partitioning.Value);

        app.MapPost("/parties/{partyId}/rsvp", async (string partyId, RsvpRequest req, CosmosClient cosmos, TimeProvider clock) =>
        {
            if (req.Answers is not { Count: > 0 } answers
                || answers.Any(a => string.IsNullOrEmpty(a.GuestId) || !Allowed.Contains(a.Rsvp)))
                return Results.BadRequest("Answer attending or declined for at least one guest.");

            var container = cosmos.GetContainer(app.Configuration["CosmosDatabase"] ?? "wedding", app.Configuration["PartiesContainer"] ?? "parties");
            Party party;
            string etag;
            try
            {
                var read = await container.ReadItemAsync<Party>(partyId, pk);
                party = read.Resource;
                etag = read.ETag;
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                return Results.NotFound();
            }

            var ids = party.Guests.Select(g => g.Id).ToHashSet();
            if (answers.Any(a => !ids.Contains(a.GuestId!)))
                return Results.BadRequest("A guest does not belong to this party.");

            var now = clock.GetUtcNow().UtcDateTime;
            // Last answer wins if a guest is listed twice
            var byGuest = answers.GroupBy(a => a.GuestId!).ToDictionary(g => g.Key, g => g.Last().Rsvp!);
            var updated = party with
            {
                Guests = party.Guests.Select(g => byGuest.TryGetValue(g.Id, out var r)
                    ? g with { Rsvp = r, RsvpChangedAt = now, RsvpChangedBy = "guest" }
                    : g).ToList()
            };

            try
            {
                await container.ReplaceItemAsync(updated, updated.Id, pk, new ItemRequestOptions { IfMatchEtag = etag });
                return Results.NoContent();
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return Results.Conflict("This party was just changed elsewhere. Please try again.");
            }
        }).RequireAuthorization(Policies.Guest);
    }
}

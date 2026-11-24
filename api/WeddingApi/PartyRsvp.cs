using System.Net;
using Microsoft.Azure.Cosmos;

public record RsvpAnswer(string? GuestId, string? Rsvp);
public record RsvpRequest(List<RsvpAnswer>? Answers);

public static class PartyRsvp
{
    public static void MapPartyRsvp(this WebApplication app)
    {
        var pk = new PartitionKey(Partitioning.Value);

        app.MapPost("/parties/{partyId}/rsvp", async (string partyId, RsvpRequest req, CosmosClient cosmos, TimeProvider clock) =>
        {
            if (req.Answers is not { Count: > 0 } answers)
                return Results.BadRequest("Answer attending or declined for at least one guest.");

            var container = cosmos.Container(app.Configuration);
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

            var deadline = await SettingsStore.GetRsvpDeadline(container);
            var result = party.AnswerAsGuest(answers, clock.GetUtcNow().UtcDateTime, deadline);
            if (result.Refusal is { } refusal) return refusal.ToHttp();

            var updated = result.Value;
            try
            {
                await container.ReplaceItemAsync(updated, updated.Id, pk, new ItemRequestOptions { IfMatchEtag = etag });
                return Results.NoContent();
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return Results.Conflict(RefusalResults.ChangedElsewhere);
            }
        }).RequireAuthorization(Policies.Guest);
    }
}

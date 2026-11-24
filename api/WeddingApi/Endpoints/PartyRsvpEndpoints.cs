public record RsvpAnswer(string? GuestId, string? Rsvp);
public record RsvpRequest(List<RsvpAnswer>? Answers);

public static class PartyRsvpEndpoints
{
    public static void MapPartyRsvp(this WebApplication app)
    {
        app.MapPost("/parties/{partyId}/rsvp", async (string partyId, RsvpRequest req, PartyStore store, SettingsStore settings, TimeProvider clock) =>
        {
            if (req.Answers is not { Count: > 0 } answers)
                return Results.BadRequest("Answer attending or declined for at least one guest.");

            var loaded = await store.Load(partyId);
            if (loaded.Refusal is { } missing) return missing.ToHttp();

            var answered = loaded.Value.AnswerAsGuest(answers, clock.GetUtcNow().UtcDateTime, await settings.GetRsvpDeadline());
            if (answered.Refusal is { } refusal) return refusal.ToHttp();

            if (await store.Save(new PartyChange.Updated(answered.Value)) is { } refused) return refused.ToHttp();
            return Results.NoContent();
        }).RequireAuthorization(Policies.Guest);
    }
}

public record AdminRsvpRequest(string? Rsvp);

public static class AdminRsvpEndpoints
{
    public static void MapAdminRsvp(this WebApplication app)
    {
        app.MapPut("/admin/guests/{guestId}/rsvp", async (string guestId, AdminRsvpRequest req, PartyStore store, TimeProvider clock) =>
        {
            var loaded = await store.LoadByGuest(guestId);
            if (loaded.Refusal is { } missing) return missing.ToHttp();

            var set = loaded.Value.SetRsvpAsAdmin(guestId, req.Rsvp, clock.GetUtcNow().UtcDateTime);
            if (set.Refusal is { } refusal) return refusal.ToHttp();

            if (await store.Save(new PartyChange.Updated(set.Value)) is { } refused) return refused.ToHttp();
            return Results.Ok(PartyDto.From(set.Value));
        }).RequireAuthorization(Policies.Admin);
    }
}

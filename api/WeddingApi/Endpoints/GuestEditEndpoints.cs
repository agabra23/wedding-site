public record RenameGuestRequest(string? FirstName, string? LastName);
public record MoveGuestRequest(string? PartyId);

public static class GuestEditEndpoints
{
    public static void MapGuestEdit(this WebApplication app)
    {
        app.MapPatch("/admin/guests/{guestId}", async (string guestId, RenameGuestRequest req, PartyStore store) =>
        {
            if (req.FirstName is null && req.LastName is null)
                return Results.BadRequest("Names can't be blank.");

            var loaded = await store.LoadByGuest(guestId);
            if (loaded.Refusal is { } missing) return missing.ToHttp();

            var renamed = (await store.GuestList()).Rename(loaded.Value, guestId, req.FirstName, req.LastName);
            if (renamed.Refusal is { } refusal) return refusal.ToHttp();

            if (await store.Save(new PartyChange.Updated(renamed.Value)) is { } refused) return refused.ToHttp();
            return Results.Ok(PartyDto.From(renamed.Value));
        }).RequireAuthorization(Policies.Admin);

        app.MapDelete("/admin/guests/{guestId}", async (string guestId, PartyStore store) =>
        {
            var loaded = await store.LoadByGuest(guestId);
            if (loaded.Refusal is { } missing) return missing.ToHttp();

            var removed = loaded.Value.Remove(guestId);
            if (removed.Refusal is { } refusal) return refusal.ToHttp();

            if (await store.Save(removed.Value) is { } refused) return refused.ToHttp();
            return Results.NoContent();
        }).RequireAuthorization(Policies.Admin);

        app.MapPost("/admin/guests/{guestId}/move", async (string guestId, MoveGuestRequest? req, PartyStore store) =>
        {
            var source = await store.LoadByGuest(guestId);
            if (source.Refusal is { } missing) return missing.ToHttp();

            Party? dest = null;
            if (!string.IsNullOrWhiteSpace(req?.PartyId))
            {
                var loaded = await store.Load(req.PartyId);
                if (loaded.Refusal is { } noDest) return noDest.ToHttp();
                dest = loaded.Value;
            }

            var moved = source.Value.Move(guestId, dest);
            if (moved.Refusal is { } refusal) return refusal.ToHttp();

            if (await store.Save(moved.Value) is { } refused) return refused.ToHttp();
            return Results.Ok(PartyDto.From(moved.Value.To.Party));
        }).RequireAuthorization(Policies.Admin);
    }
}

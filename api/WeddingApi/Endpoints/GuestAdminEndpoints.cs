public record GuestName(string? FirstName, string? LastName);
public record AddGuestsRequest(string? PartyId, List<GuestName>? Guests);

public static class GuestAdminEndpoints
{
    public static void MapGuestAdmin(this WebApplication app)
    {
        app.MapGet("/admin/parties", async (PartyStore store) =>
            Results.Ok((await store.All()).Select(PartyDto.From)))
            .RequireAuthorization(Policies.Admin);

        app.MapPost("/admin/guests", async (AddGuestsRequest req, PartyStore store) =>
        {
            if (req.Guests is not { Count: > 0 } names)
                return Results.BadRequest("Every guest needs a first and last name.");

            Party? into = null;
            if (!string.IsNullOrWhiteSpace(req.PartyId))
            {
                var loaded = await store.Load(req.PartyId);
                if (loaded.Refusal is { } missing) return missing.ToHttp();
                into = loaded.Value;
            }

            var added = (await store.GuestList()).Add(names, into);
            if (added.Refusal is { } refusal) return refusal.ToHttp();

            if (await store.Save(added.Value) is { } refused) return refused.ToHttp();
            var saved = added.Value.Party;
            return Results.Created($"/admin/parties/{saved.Id}", PartyDto.From(saved));
        }).RequireAuthorization(Policies.Admin);
    }
}

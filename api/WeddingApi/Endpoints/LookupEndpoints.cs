public record LookupRequest(string? FirstName, string? LastName);

public static class LookupEndpoints
{
    public static void MapLookup(this WebApplication app)
    {
        app.MapPost("/lookup", async (LookupRequest req, PartyStore store, SettingsStore settings) =>
        {
            if (string.IsNullOrWhiteSpace(req.FirstName) || string.IsNullOrWhiteSpace(req.LastName))
                return Results.NotFound();

            if (await store.FindByName(req.FirstName, req.LastName) is not { } party)
                return Results.NotFound();
            return Results.Ok(new { party = PartyDto.From(party), rsvpDeadline = await settings.GetRsvpDeadline() });
        }).RequireAuthorization(Policies.Guest);
    }
}

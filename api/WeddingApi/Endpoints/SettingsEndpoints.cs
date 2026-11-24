using System.Globalization;

public record SettingsRequest(string? RsvpDeadline);

public static class SettingsEndpoints
{
    public static void MapSettingsAdmin(this WebApplication app)
    {
        app.MapGet("/admin/settings", async (SettingsStore settings) =>
            Results.Ok(new { rsvpDeadline = await settings.GetRsvpDeadline() }))
            .RequireAuthorization(Policies.Admin);

        app.MapPut("/admin/settings", async (SettingsRequest req, SettingsStore settings) =>
        {
            DateTime? deadline = null;
            if (req.RsvpDeadline is { } raw)
            {
                // Times without an offset are read as UTC, so the stored moment is never ambiguous
                if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                    return Results.BadRequest("The RSVP deadline must be a date and time.");
                deadline = parsed.UtcDateTime;
            }

            await settings.SetRsvpDeadline(deadline);
            return Results.Ok(new { rsvpDeadline = deadline });
        }).RequireAuthorization(Policies.Admin);
    }
}

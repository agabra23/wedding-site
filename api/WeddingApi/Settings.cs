using System.Globalization;
using System.Net;
using Microsoft.Azure.Cosmos;

/// <summary>The single Settings document (ADR-0001). It shares the Parties container and partition.</summary>
public record Settings(string Id, DateTime? RsvpDeadline)
{
    public const string DocumentId = "settings";
    public string Pk { get; init; } = Partitioning.Value;
}

public record SettingsRequest(string? RsvpDeadline);

public static class SettingsStore
{
    public static Container Container(this CosmosClient cosmos, IConfiguration config) =>
        cosmos.GetContainer(config["CosmosDatabase"] ?? "wedding", config["PartiesContainer"] ?? "parties");

    /// <summary>The RSVP Deadline, or null when RSVPs are open.</summary>
    public static async Task<DateTime?> GetRsvpDeadline(Container container)
    {
        try
        {
            var read = await container.ReadItemAsync<Settings>(Settings.DocumentId, new PartitionKey(Partitioning.Value));
            return read.Resource.RsvpDeadline is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
        }
        catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public static bool IsPast(DateTime? deadline, TimeProvider clock) =>
        deadline is { } d && clock.GetUtcNow().UtcDateTime > d;
}

public static class SettingsAdmin
{
    public static void MapSettingsAdmin(this WebApplication app)
    {
        app.MapGet("/admin/settings", async (CosmosClient cosmos) =>
            Results.Ok(new { rsvpDeadline = await SettingsStore.GetRsvpDeadline(cosmos.Container(app.Configuration)) }))
            .RequireAuthorization(Policies.Admin);

        app.MapPut("/admin/settings", async (SettingsRequest req, CosmosClient cosmos) =>
        {
            DateTime? deadline = null;
            if (req.RsvpDeadline is { } raw)
            {
                // Times without an offset are read as UTC, so the stored moment is never ambiguous
                if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                    return Results.BadRequest("The RSVP deadline must be a date and time.");
                deadline = parsed.UtcDateTime;
            }

            await cosmos.Container(app.Configuration).UpsertItemAsync(
                new Settings(Settings.DocumentId, deadline), new PartitionKey(Partitioning.Value));
            return Results.Ok(new { rsvpDeadline = deadline });
        }).RequireAuthorization(Policies.Admin);
    }
}

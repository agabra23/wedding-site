using System.Net;
using Microsoft.Azure.Cosmos;

/// <summary>The single Settings document (ADR-0001). It shares the Parties container and partition.</summary>
public record Settings(string Id, DateTime? RsvpDeadline)
{
    public const string DocumentId = "settings";
    public string Pk { get; init; } = Partitioning.Value;
}

public sealed class SettingsStore(Database db)
{
    /// <summary>The RSVP Deadline, or null when RSVPs are open.</summary>
    public async Task<DateTime?> GetRsvpDeadline()
    {
        try
        {
            var read = await db.Container.ReadItemAsync<Settings>(Settings.DocumentId, Database.Key);
            return read.Resource.RsvpDeadline is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
        }
        catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task SetRsvpDeadline(DateTime? deadline) =>
        await db.Container.UpsertItemAsync(new Settings(Settings.DocumentId, deadline), Database.Key);
}

using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Azure.Cosmos;

public static class Rsvps
{
    public const string Pending = "pending";
}

public record Guest(
    string Id,
    string FirstName,
    string LastName,
    string FirstNameNormalized,
    string LastNameNormalized,
    string Rsvp,
    DateTime? RsvpChangedAt,
    string? RsvpChangedBy);

public record Party(string Id, List<Guest> Guests)
{
    public string Pk { get; init; } = Partitioning.Value;
}

public record GuestName(string? FirstName, string? LastName);
public record AddGuestsRequest(string? PartyId, List<GuestName>? Guests);

public record GuestDto(string Id, string FirstName, string LastName, string Rsvp, DateTime? RsvpChangedAt, string? RsvpChangedBy);
public record PartyDto(string Id, List<GuestDto> Guests)
{
    public static PartyDto From(Party p) => new(p.Id,
        p.Guests.Select(g => new GuestDto(g.Id, g.FirstName, g.LastName, g.Rsvp, g.RsvpChangedAt, g.RsvpChangedBy)).ToList());
}

public static class Names
{
    /// <summary>Trims, ignores capitals and removes accents (ñ becomes n).</summary>
    public static string Normalize(string s)
    {
        var decomposed = s.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}

public static class GuestAdmin
{
    public static void MapGuestAdmin(this WebApplication app)
    {
        // ADR-0001: every document shares one partition key value
        var pk = new PartitionKey(Partitioning.Value);

        Container Parties(CosmosClient c) =>
            c.GetContainer(app.Configuration["CosmosDatabase"] ?? "wedding", app.Configuration["PartiesContainer"] ?? "parties");

        async Task<List<Party>> AllParties(Container container)
        {
            var it = container.GetItemQueryIterator<Party>("SELECT * FROM c WHERE IS_DEFINED(c.guests)",
                requestOptions: new QueryRequestOptions { PartitionKey = pk });
            var all = new List<Party>();
            while (it.HasMoreResults) all.AddRange(await it.ReadNextAsync());
            return all;
        }

        app.MapGet("/admin/parties", async (CosmosClient cosmos) =>
            Results.Ok((await AllParties(Parties(cosmos))).Select(PartyDto.From)))
            .RequireAuthorization(Policies.Admin);

        app.MapPost("/admin/guests", async (AddGuestsRequest req, CosmosClient cosmos) =>
        {
            if (req.Guests is not { Count: > 0 } names
                || names.Any(n => string.IsNullOrWhiteSpace(n.FirstName) || string.IsNullOrWhiteSpace(n.LastName)))
                return Results.BadRequest("Every guest needs a first and last name.");

            var newGuests = names.Select(n => new Guest(
                Guid.NewGuid().ToString("N"),
                n.FirstName!, n.LastName!,
                Names.Normalize(n.FirstName!), Names.Normalize(n.LastName!),
                Rsvps.Pending, null, null)).ToList();

            var container = Parties(cosmos);
            Party? existing = null;
            string? etag = null;
            if (!string.IsNullOrWhiteSpace(req.PartyId))
            {
                try
                {
                    var read = await container.ReadItemAsync<Party>(req.PartyId, pk);
                    existing = read.Resource;
                    etag = read.ETag;
                }
                catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    return Results.NotFound();
                }
            }

            var taken = (await AllParties(container)).SelectMany(p => p.Guests)
                .Select(g => (g.FirstNameNormalized, g.LastNameNormalized)).ToHashSet();
            foreach (var g in newGuests)
                if (!taken.Add((g.FirstNameNormalized, g.LastNameNormalized)))
                    return Results.Conflict($"A guest named {g.FirstName.Trim()} {g.LastName.Trim()} already exists.");

            try
            {
                Party saved;
                if (existing is null)
                {
                    saved = new Party(Guid.NewGuid().ToString("N"), newGuests);
                    await container.CreateItemAsync(saved, pk);
                }
                else
                {
                    saved = existing with { Guests = [.. existing.Guests, .. newGuests] };
                    await container.ReplaceItemAsync(saved, saved.Id, pk, new ItemRequestOptions { IfMatchEtag = etag });
                }
                return Results.Created($"/admin/parties/{saved.Id}", PartyDto.From(saved));
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return Results.Conflict("This party was just changed elsewhere. Please try again.");
            }
        }).RequireAuthorization(Policies.Admin);
    }
}

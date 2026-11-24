using System.Net;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Loads and saves Parties for one request. It remembers the ETag of every Party it loads and writes with
/// If-Match, so a change made elsewhere in between is refused (<see cref="Refusal.ChangedElsewhere"/>) instead of
/// overwritten. Register it scoped: one instance is one read-modify-write session.
/// Any other Cosmos failure (throttling, timeouts) is thrown and becomes a 500; nothing retries.
/// </summary>
public sealed class PartyStore(Database db)
{
    const string AllPartiesQuery = "SELECT * FROM c WHERE IS_DEFINED(c.guests)";

    readonly Dictionary<string, (Party Party, string ETag)> tracked = [];

    Container Container => db.Container;

    // Reads

    public async Task<IReadOnlyList<Party>> All()
    {
        var it = Container.GetItemQueryIterator<Party>(AllPartiesQuery, requestOptions: Options());
        var all = new List<Party>();
        while (it.HasMoreResults) all.AddRange(await it.ReadNextAsync());
        return all;
    }

    /// <summary>Every Guest, for unique-name checks. Not tracked: nothing here can be saved.</summary>
    public async Task<GuestList> GuestList() => new(await All());

    public async Task<Result<Party>> LoadByGuest(string guestId)
    {
        var query = new QueryDefinition(
            "SELECT VALUE c.id FROM c WHERE EXISTS(SELECT VALUE g FROM g IN c.guests WHERE g.id = @id)")
            .WithParameter("@id", guestId);
        var it = Container.GetItemQueryIterator<string>(query, requestOptions: Options());
        string? partyId = null;
        while (partyId is null && it.HasMoreResults)
            partyId = (await it.ReadNextAsync()).FirstOrDefault();
        return partyId is null ? new Refusal.NotFound() : await Load(partyId);
    }

    /// <summary>
    /// A Party already loaded in this session comes back as the same instance, without another read.
    /// A Party that is gone, including one removed since a query found it, is NotFound.
    /// </summary>
    public async Task<Result<Party>> Load(string partyId)
    {
        if (tracked.TryGetValue(partyId, out var known)) return known.Party;
        try
        {
            var read = await Container.ReadItemAsync<Party>(partyId, Database.Key);
            tracked[partyId] = (read.Resource, read.ETag);
            return read.Resource;
        }
        catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return new Refusal.NotFound();
        }
    }

    /// <summary>The Party holding a Guest with this name, ignoring capitals and accents. Not tracked.</summary>
    public async Task<Party?> FindByName(string firstName, string lastName)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE EXISTS(SELECT VALUE g FROM g IN c.guests WHERE g.firstNameNormalized = @f AND g.lastNameNormalized = @l)")
            .WithParameter("@f", Names.Normalize(firstName)).WithParameter("@l", Names.Normalize(lastName));
        var it = Container.GetItemQueryIterator<Party>(query, requestOptions: Options());
        while (it.HasMoreResults)
            foreach (var party in await it.ReadNextAsync())
                return party;
        return null;
    }

    // Writes: null means saved

    public async Task<Refusal?> Save(PartyChange change)
    {
        try
        {
            switch (change)
            {
                case PartyChange.Created c:
                    var created = await Container.CreateItemAsync(c.Party, Database.Key);
                    tracked[c.Party.Id] = (c.Party, created.ETag);
                    break;
                case PartyChange.Updated u:
                    var replaced = await Container.ReplaceItemAsync(u.Party, u.Party.Id, Database.Key,
                        new ItemRequestOptions { IfMatchEtag = ETagOf(u.Party) });
                    tracked[u.Party.Id] = (u.Party, replaced.ETag);
                    break;
                case PartyChange.Gone g:
                    await Container.DeleteItemAsync<Party>(g.Party.Id, Database.Key,
                        new ItemRequestOptions { IfMatchEtag = ETagOf(g.Party) });
                    tracked.Remove(g.Party.Id);
                    break;
                case PartyChange.Unchanged:
                    break;
            }
            return null;
        }
        catch (CosmosException e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            return new Refusal.ChangedElsewhere();
        }
    }

    /// <summary>ADR-0001: one transactional batch, so the Guest is never in two Parties or none.</summary>
    public async Task<Refusal?> Save(GuestMove move)
    {
        if (move.From is PartyChange.Unchanged && move.To is PartyChange.Unchanged) return null;

        var batch = Container.CreateTransactionalBatch(Database.Key);
        Add(batch, move.From);
        Add(batch, move.To);

        using var response = await batch.ExecuteAsync();
        if (response.IsSuccessStatusCode)
        {
            Track(move.From, response[0].ETag);
            Track(move.To, response[1].ETag);
            return null;
        }

        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict
            || Enumerable.Range(0, response.Count).Any(i => response[i].StatusCode == HttpStatusCode.PreconditionFailed))
            return new Refusal.ChangedElsewhere();

        throw new CosmosException(response.ErrorMessage ?? "Transactional batch failed.", response.StatusCode, 0,
            response.ActivityId, response.RequestCharge);
    }

    void Add(TransactionalBatch batch, PartyChange change)
    {
        switch (change)
        {
            case PartyChange.Created c:
                batch.CreateItem(c.Party);
                break;
            case PartyChange.Updated u:
                batch.ReplaceItem(u.Party.Id, u.Party, new TransactionalBatchItemRequestOptions { IfMatchEtag = ETagOf(u.Party) });
                break;
            case PartyChange.Gone g:
                batch.DeleteItem(g.Party.Id, new TransactionalBatchItemRequestOptions { IfMatchEtag = ETagOf(g.Party) });
                break;
            case PartyChange.Unchanged:
                throw new InvalidOperationException("An unchanged Party can't be part of a batch.");
        }
    }

    void Track(PartyChange change, string? etag)
    {
        switch (change)
        {
            case PartyChange.Created or PartyChange.Updated:
                tracked[change.Party.Id] = (change.Party, etag!);
                break;
            case PartyChange.Gone:
                tracked.Remove(change.Party.Id);
                break;
        }
    }

    string ETagOf(Party party) =>
        tracked.TryGetValue(party.Id, out var known)
            ? known.ETag
            : throw new InvalidOperationException($"Party {party.Id} was never loaded in this session, so it can't be saved.");

    static QueryRequestOptions Options() => new() { PartitionKey = Database.Key };
}

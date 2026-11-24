using Microsoft.Azure.Cosmos;

namespace WeddingApi.Tests;

/// <summary>Hosts a throwaway database on the Cosmos emulator; each <see cref="Session"/> is a separate request.</summary>
public sealed class StoreFixture : IAsyncLifetime
{
    readonly string connection =
        Environment.GetEnvironmentVariable("COSMOS_TEST_CONNECTION") ?? ApiFactory.DefaultConnection;

    readonly string databaseName = "test-" + Guid.NewGuid().ToString("N");
    CosmosClient client = null!;

    public Database Db { get; private set; } = null!;

    /// <summary>A new session, as one HTTP request would get.</summary>
    public PartyStore Session() => new(Db);

    public async Task InitializeAsync()
    {
        client = new CosmosClient(connection, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            SerializerOptions = new() { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase },
        });
        var db = (await client.CreateDatabaseAsync(databaseName)).Database;
        await db.CreateContainerAsync(new ContainerProperties("parties", Partitioning.Path));
        Db = new Database(client, databaseName, "parties");
    }

    public async Task DisposeAsync()
    {
        await client.GetDatabase(databaseName).DeleteAsync();
        client.Dispose();
    }
}

public class PartyStoreTests(StoreFixture fixture) : IClassFixture<StoreFixture>
{
    static Party NewParty(params string[] firstNames) => Party.New(firstNames.Select(f => Guest.New(f, "Smith")));

    async Task<Party> Seed(params string[] firstNames)
    {
        var party = NewParty(firstNames);
        Assert.Null(await fixture.Session().Save(new PartyChange.Created(party)));
        return party;
    }

    async Task<Party?> Stored(string partyId) =>
        (await fixture.Session().Load(partyId)) is { Refusal: null } r ? r.Value : null;

    async Task<string> ETag(string partyId) =>
        (await fixture.Db.Container.ReadItemAsync<Party>(partyId, Database.Key)).ETag;

    static PartyChange AddGuest(Party party, string firstName) =>
        new PartyChange.Updated(party with { Guests = [.. party.Guests, Guest.New(firstName, "Smith")] });

    // Each PartyChange

    [Fact]
    public async Task Created_persists_the_party()
    {
        var party = await Seed("A", "B");

        Assert.Equal(party.Guests.Select(g => g.Id), (await Stored(party.Id))!.Guests.Select(g => g.Id));
    }

    [Fact]
    public async Task Updated_replaces_the_party()
    {
        var seeded = await Seed("A");
        var store = fixture.Session();
        var loaded = (await store.Load(seeded.Id)).Value;
        var answered = loaded.AnswerAsGuest([new(loaded.Guests[0].Id, Rsvps.Attending)], DateTime.UtcNow, null).Value;

        Assert.Null(await store.Save(new PartyChange.Updated(answered)));

        Assert.Equal(Rsvps.Attending, (await Stored(seeded.Id))!.Guests[0].Rsvp);
    }

    [Fact]
    public async Task Gone_deletes_the_document()
    {
        var seeded = await Seed("A");
        var store = fixture.Session();
        var loaded = (await store.Load(seeded.Id)).Value;

        Assert.Null(await store.Save(new PartyChange.Gone(loaded)));

        Assert.IsType<Refusal.NotFound>((await fixture.Session().Load(seeded.Id)).Refusal);
    }

    [Fact]
    public async Task Unchanged_writes_nothing()
    {
        var seeded = await Seed("A");
        var before = await ETag(seeded.Id);
        var store = fixture.Session();
        var loaded = (await store.Load(seeded.Id)).Value;

        Assert.Null(await store.Save(new PartyChange.Unchanged(loaded)));

        Assert.Equal(before, await ETag(seeded.Id));
    }

    [Fact]
    public async Task A_session_can_keep_saving_the_party_it_saved()
    {
        var seeded = await Seed("A");
        var store = fixture.Session();
        var loaded = (await store.Load(seeded.Id)).Value;
        var first = loaded with { Guests = [.. loaded.Guests, Guest.New("B", "Smith")] };
        var second = first with { Guests = [.. first.Guests, Guest.New("C", "Smith")] };

        Assert.Null(await store.Save(new PartyChange.Updated(first)));
        Assert.Null(await store.Save(new PartyChange.Updated(second)));

        Assert.Equal(3, (await Stored(seeded.Id))!.Guests.Count);
    }

    // Moves

    [Fact]
    public async Task A_move_saves_both_parties()
    {
        var source = await Seed("A", "B");
        var dest = await Seed("C");
        var store = fixture.Session();
        var from = (await store.Load(source.Id)).Value;
        var into = (await store.Load(dest.Id)).Value;

        var move = from.Move(from.Guests[0].Id, into).Value;
        Assert.Null(await store.Save(move));

        Assert.Equal(["B"], (await Stored(source.Id))!.Guests.Select(g => g.FirstName));
        Assert.Equal(["C", "A"], (await Stored(dest.Id))!.Guests.Select(g => g.FirstName));
    }

    [Fact]
    public async Task Moving_the_only_guest_deletes_the_old_party_and_creates_the_new_one()
    {
        var source = await Seed("A");
        var store = fixture.Session();
        var from = (await store.Load(source.Id)).Value;

        var move = from.Move(from.Guests[0].Id, null).Value;
        Assert.Null(await store.Save(move));

        Assert.Null(await Stored(source.Id));
        Assert.Equal(["A"], (await Stored(move.To.Party.Id))!.Guests.Select(g => g.FirstName));
    }

    [Fact]
    public async Task A_move_whose_destination_changed_elsewhere_is_refused_and_leaves_the_source_alone()
    {
        var source = await Seed("A", "B");
        var dest = await Seed("C");
        var store = fixture.Session();
        var from = (await store.Load(source.Id)).Value;
        var into = (await store.Load(dest.Id)).Value;
        var move = from.Move(from.Guests[0].Id, into).Value;

        // Another request changes the destination after this session loaded it
        var other = fixture.Session();
        Assert.Null(await other.Save(AddGuest((await other.Load(dest.Id)).Value, "D")));

        Assert.IsType<Refusal.ChangedElsewhere>(await store.Save(move));

        Assert.Equal(["A", "B"], (await Stored(source.Id))!.Guests.Select(g => g.FirstName));
        Assert.Equal(["C", "D"], (await Stored(dest.Id))!.Guests.Select(g => g.FirstName));
    }

    // Concurrency

    [Fact]
    public async Task When_two_sessions_load_the_same_party_the_second_save_is_refused()
    {
        var seeded = await Seed("A");
        var first = fixture.Session();
        var second = fixture.Session();
        var a = (await first.Load(seeded.Id)).Value;
        var b = (await second.Load(seeded.Id)).Value;

        Assert.Null(await first.Save(AddGuest(a, "B")));
        Assert.IsType<Refusal.ChangedElsewhere>(await second.Save(AddGuest(b, "C")));

        Assert.Equal(["A", "B"], (await Stored(seeded.Id))!.Guests.Select(g => g.FirstName));
    }

    [Fact]
    public async Task Deleting_a_party_changed_elsewhere_is_refused()
    {
        var seeded = await Seed("A");
        var first = fixture.Session();
        var second = fixture.Session();
        var a = (await first.Load(seeded.Id)).Value;
        var b = (await second.Load(seeded.Id)).Value;
        Assert.Null(await first.Save(AddGuest(a, "B")));

        Assert.IsType<Refusal.ChangedElsewhere>(await second.Save(new PartyChange.Gone(b)));

        Assert.NotNull(await Stored(seeded.Id));
    }

    // Loading

    [Fact]
    public async Task A_guest_removed_after_the_lookup_is_not_found()
    {
        var seeded = await Seed("A", "B");
        var guestId = seeded.Guests[1].Id;
        var store = fixture.Session();
        var loaded = (await store.Load(seeded.Id)).Value;
        Assert.Null(await store.Save(new PartyChange.Updated(loaded with { Guests = loaded.Guests.Take(1).ToList() })));

        Assert.IsType<Refusal.NotFound>((await fixture.Session().LoadByGuest(guestId)).Refusal);
    }

    [Fact]
    public async Task A_party_deleted_between_the_query_and_the_read_is_not_found()
    {
        var seeded = await Seed("A");
        await fixture.Db.Container.DeleteItemAsync<Party>(seeded.Id, Database.Key);

        // Load is the read that follows a query hit, so a Party gone by then must be NotFound, not an exception
        Assert.IsType<Refusal.NotFound>((await fixture.Session().Load(seeded.Id)).Refusal);
        Assert.IsType<Refusal.NotFound>((await fixture.Session().LoadByGuest(seeded.Guests[0].Id)).Refusal);
    }

    [Fact]
    public async Task Loading_a_party_by_guest_finds_it()
    {
        var seeded = await Seed("A", "B");

        var loaded = await fixture.Session().LoadByGuest(seeded.Guests[1].Id);

        Assert.Equal(seeded.Id, loaded.Value.Id);
    }

    [Fact]
    public async Task Loading_the_same_id_twice_returns_the_same_instance_without_reading_again()
    {
        var seeded = await Seed("A");
        var store = fixture.Session();
        var first = (await store.Load(seeded.Id)).Value;

        // Changed elsewhere after the first load: a second read would show it
        var other = fixture.Session();
        Assert.Null(await other.Save(AddGuest((await other.Load(seeded.Id)).Value, "B")));

        var second = (await store.Load(seeded.Id)).Value;

        Assert.Same(first, second);
        Assert.Single(second.Guests);
    }

    [Fact]
    public async Task Loading_by_guest_then_by_id_returns_the_same_instance()
    {
        var seeded = await Seed("A");
        var store = fixture.Session();

        var byGuest = (await store.LoadByGuest(seeded.Guests[0].Id)).Value;
        var byId = (await store.Load(seeded.Id)).Value;

        Assert.Same(byGuest, byId);
    }

    // Misuse

    [Fact]
    public async Task Saving_a_party_this_session_never_loaded_throws()
    {
        var seeded = await Seed("A");
        var loaded = (await fixture.Session().Load(seeded.Id)).Value;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session().Save(new PartyChange.Updated(loaded)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session().Save(new PartyChange.Gone(loaded)));
    }

    // Queries

    [Fact]
    public async Task FindByName_ignores_capitals_and_accents()
    {
        var party = Party.New([Guest.New("Zoë", "Zzyzx")]);
        Assert.Null(await fixture.Session().Save(new PartyChange.Created(party)));

        var found = await fixture.Session().FindByName(" ZOE ", "zzyzx");

        Assert.Equal(party.Id, found!.Id);
        Assert.Null(await fixture.Session().FindByName("Nobody", "Zzyzx"));
    }

    [Fact]
    public async Task All_returns_parties_but_not_other_documents()
    {
        var party = await Seed("A");
        await new SettingsStore(fixture.Db).SetRsvpDeadline(DateTime.UtcNow);

        var all = await fixture.Session().All();

        Assert.Contains(all, p => p.Id == party.Id);
        Assert.DoesNotContain(all, p => p.Id == Settings.DocumentId);
    }
}

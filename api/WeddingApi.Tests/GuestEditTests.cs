using System.Net;
using System.Net.Http.Json;

namespace WeddingApi.Tests;

public class GuestEditTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    record AddRequest(string? PartyId, GuestName[] Guests);
    record Name(string? FirstName, string? LastName);
    record LookupBody(string FirstName, string LastName);
    record LookupResponse(PartyDto Party);

    static string Unique() => Guid.NewGuid().ToString("N");

    async Task<PartyDto> Seed(string last)
    {
        var admin = await api.SignedInClientAsync(ApiFactory.AdminPassword);
        var res = await admin.PostAsJsonAsync("/admin/guests", new AddRequest(null, [new GuestName("Ann", last), new GuestName("Bob", last)]));
        return (await res.Content.ReadFromJsonAsync<PartyDto>())!;
    }

    async Task<HttpResponseMessage> Rename(string guestId, Name body, string password = ApiFactory.AdminPassword) =>
        await (await api.SignedInClientAsync(password)).PatchAsJsonAsync($"/admin/guests/{guestId}", body);

    async Task<HttpResponseMessage> Remove(string guestId, string password = ApiFactory.AdminPassword) =>
        await (await api.SignedInClientAsync(password)).DeleteAsync($"/admin/guests/{guestId}");

    async Task<HttpStatusCode> Lookup(string first, string last) =>
        (await (await api.SignedInClientAsync(ApiFactory.EventPassword)).PostAsJsonAsync("/lookup", new LookupBody(first, last))).StatusCode;

    [Fact]
    public async Task Rename_works_for_lookup_with_new_name_not_old()
    {
        var u = Unique();
        var p = await Seed(u);
        var res = await Rename(p.Guests[0].Id, new Name("Anna", null));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var party = (await res.Content.ReadFromJsonAsync<PartyDto>())!;
        Assert.Equal("Anna", party.Guests[0].FirstName);
        Assert.Equal(u, party.Guests[0].LastName);

        Assert.Equal(HttpStatusCode.OK, await Lookup("Anna", u));
        Assert.Equal(HttpStatusCode.NotFound, await Lookup("Ann", u));
    }

    [Fact]
    public async Task Rename_to_another_guests_name_is_409()
    {
        var u = Unique();
        var p = await Seed(u);
        Assert.Equal(HttpStatusCode.Conflict, (await Rename(p.Guests[0].Id, new Name("  BOB ", null))).StatusCode);
    }

    [Fact]
    public async Task Changing_only_capitals_or_accents_of_own_name_succeeds()
    {
        var u = Unique();
        var p = await Seed(u);
        Assert.Equal(HttpStatusCode.OK, (await Rename(p.Guests[0].Id, new Name("ANN", null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Rename(p.Guests[0].Id, new Name("Ánn", null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await Lookup("ann", u));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData(null, "  ")]
    [InlineData(null, null)]
    public async Task Blank_is_400(string? first, string? last)
    {
        var p = await Seed(Unique());
        Assert.Equal(HttpStatusCode.BadRequest, (await Rename(p.Guests[0].Id, new Name(first, last))).StatusCode);
    }

    [Fact]
    public async Task Unknown_guest_is_404_for_rename_and_remove()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Rename(Unique(), new Name("X", null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Remove(Unique())).StatusCode);
    }

    [Fact]
    public async Task Removing_a_guest_takes_them_out_of_the_party()
    {
        var u = Unique();
        var p = await Seed(u);
        Assert.Equal(HttpStatusCode.NoContent, (await Remove(p.Guests[0].Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await Lookup("Ann", u));

        var admin = await api.SignedInClientAsync(ApiFactory.AdminPassword);
        var parties = (await admin.GetFromJsonAsync<List<PartyDto>>("/admin/parties"))!;
        var party = Assert.Single(parties, x => x.Id == p.Id);
        Assert.Equal(["Bob"], party.Guests.Select(g => g.FirstName));
    }

    [Fact]
    public async Task Removing_last_guest_deletes_the_party()
    {
        var p = await Seed(Unique());
        Assert.Equal(HttpStatusCode.NoContent, (await Remove(p.Guests[0].Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Remove(p.Guests[1].Id)).StatusCode);

        var admin = await api.SignedInClientAsync(ApiFactory.AdminPassword);
        var parties = (await admin.GetFromJsonAsync<List<PartyDto>>("/admin/parties"))!;
        Assert.DoesNotContain(parties, x => x.Id == p.Id);
    }

    [Fact]
    public async Task Guest_tokens_are_rejected()
    {
        var p = await Seed(Unique());
        Assert.Equal(HttpStatusCode.Forbidden, (await Rename(p.Guests[0].Id, new Name("Z", null), ApiFactory.EventPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Remove(p.Guests[0].Id, ApiFactory.EventPassword)).StatusCode);
    }

    async Task<HttpResponseMessage> Move(string guestId, string? partyId, string password = ApiFactory.AdminPassword) =>
        await (await api.SignedInClientAsync(password)).PostAsJsonAsync($"/admin/guests/{guestId}/move", new { partyId });

    async Task<List<PartyDto>> Parties() =>
        (await (await api.SignedInClientAsync(ApiFactory.AdminPassword)).GetFromJsonAsync<List<PartyDto>>("/admin/parties"))!;

    [Fact]
    public async Task Move_into_existing_party_adds_and_removes_and_keeps_rsvp()
    {
        var a = await Seed(Unique());
        var b = await Seed(Unique());
        var admin = await api.SignedInClientAsync(ApiFactory.AdminPassword);
        var set = await admin.PutAsJsonAsync($"/admin/guests/{a.Guests[0].Id}/rsvp", new { rsvp = "attending" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var before = (await set.Content.ReadFromJsonAsync<PartyDto>())!.Guests[0];
        Assert.Equal("admin", before.RsvpChangedBy);

        var res = await Move(a.Guests[0].Id, b.Id);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dest = (await res.Content.ReadFromJsonAsync<PartyDto>())!;
        Assert.Equal(b.Id, dest.Id);
        Assert.Contains(dest.Guests, g => g.Id == before.Id && g.Rsvp == before.Rsvp
            && g.RsvpChangedAt == before.RsvpChangedAt && g.RsvpChangedBy == before.RsvpChangedBy);

        var parties = await Parties();
        Assert.DoesNotContain(parties.Single(x => x.Id == a.Id).Guests, g => g.Id == before.Id);
        Assert.Equal(3, parties.Single(x => x.Id == b.Id).Guests.Count);
    }

    [Fact]
    public async Task Move_without_party_creates_a_new_party_for_the_guest()
    {
        var a = await Seed(Unique());
        var res = await Move(a.Guests[0].Id, null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dest = (await res.Content.ReadFromJsonAsync<PartyDto>())!;
        Assert.NotEqual(a.Id, dest.Id);
        Assert.Equal([a.Guests[0].Id], dest.Guests.Select(g => g.Id));
        Assert.Equal([a.Guests[1].Id], (await Parties()).Single(x => x.Id == a.Id).Guests.Select(g => g.Id));
    }

    [Fact]
    public async Task Moving_the_only_guest_deletes_the_old_party()
    {
        var a = await Seed(Unique());
        var b = await Seed(Unique());
        await Remove(a.Guests[1].Id);
        Assert.Equal(HttpStatusCode.OK, (await Move(a.Guests[0].Id, b.Id)).StatusCode);
        Assert.DoesNotContain(await Parties(), x => x.Id == a.Id);
    }

    [Fact]
    public async Task Lookup_returns_the_destination_party_after_move()
    {
        var u = Unique();
        var a = await Seed(u);
        var b = await Seed(Unique());
        await Move(a.Guests[0].Id, b.Id);
        var guest = await api.SignedInClientAsync(ApiFactory.EventPassword);
        var res = await guest.PostAsJsonAsync("/lookup", new LookupBody("Ann", u));
        var body = await res.Content.ReadFromJsonAsync<LookupResponse>();
        Assert.Equal(b.Id, body!.Party.Id);
    }

    [Fact]
    public async Task Move_to_current_party_changes_nothing()
    {
        var a = await Seed(Unique());
        var res = await Move(a.Guests[0].Id, a.Id);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(a.Id, (await res.Content.ReadFromJsonAsync<PartyDto>())!.Id);
        Assert.Equal(2, (await Parties()).Single(x => x.Id == a.Id).Guests.Count);
    }

    [Fact]
    public async Task Move_unknown_guest_or_party_is_404_and_guest_tokens_rejected()
    {
        var a = await Seed(Unique());
        Assert.Equal(HttpStatusCode.NotFound, (await Move(Unique(), a.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Move(a.Guests[0].Id, Unique())).StatusCode);
        Assert.Equal(2, (await Parties()).Single(x => x.Id == a.Id).Guests.Count);
        Assert.Equal(HttpStatusCode.Forbidden, (await Move(a.Guests[0].Id, null, ApiFactory.EventPassword)).StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;

namespace WeddingApi.Tests;

public class GuestTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    record AddRequest(string? PartyId, GuestName[] Guests);

    static string Unique() => Guid.NewGuid().ToString("N");

    async Task<HttpClient> Admin() => await api.SignedInClientAsync(ApiFactory.AdminPassword);

    static Task<HttpResponseMessage> Add(HttpClient c, string? partyId, params (string First, string Last)[] guests) =>
        c.PostAsJsonAsync("/admin/guests", new AddRequest(partyId, guests.Select(g => new GuestName(g.First, g.Last)).ToArray()));

    static async Task<PartyDto> Body(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<PartyDto>())!;

    static async Task<List<PartyDto>> List(HttpClient c) => (await c.GetFromJsonAsync<List<PartyDto>>("/admin/parties"))!;

    [Fact]
    public async Task One_person_as_a_new_party()
    {
        var c = await Admin();
        var u = Unique();
        var res = await Add(c, null, ("Ada", u));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var party = await Body(res);
        Assert.False(string.IsNullOrEmpty(party.Id));
        var g = Assert.Single(party.Guests);
        Assert.False(string.IsNullOrEmpty(g.Id));
        Assert.Equal(("Ada", u, "pending"), (g.FirstName, g.LastName, g.Rsvp));
        Assert.Null(g.RsvpChangedAt);
        Assert.Null(g.RsvpChangedBy);
    }

    [Fact]
    public async Task One_and_several_people_into_an_existing_party()
    {
        var c = await Admin();
        var u = Unique();
        var party = await Body(await Add(c, null, ("A", u)));

        var one = await Add(c, party.Id, ("B", u));
        Assert.Equal(HttpStatusCode.Created, one.StatusCode);
        var several = await Body(await Add(c, party.Id, ("C", u), ("D", u)));

        Assert.Equal(party.Id, several.Id);
        Assert.Equal(["A", "B", "C", "D"], several.Guests.Select(g => g.FirstName));
        Assert.Equal(several.Guests.Select(g => g.Id), (await List(c)).Single(p => p.Id == party.Id).Guests.Select(g => g.Id));
    }

    [Fact]
    public async Task Name_matching_an_existing_guest_after_normalizing_is_409_and_saves_nothing()
    {
        var c = await Admin();
        var u = Unique();
        var existing = await Body(await Add(c, null, ("José", u)));

        var res = await Add(c, null, ("Other", u), ("  JOSE ", u.ToUpperInvariant() + " "));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        var parties = await List(c);
        Assert.DoesNotContain(parties.SelectMany(p => p.Guests), g => g.FirstName == "Other" && g.LastName == u);
        Assert.Single(parties.Single(p => p.Id == existing.Id).Guests);

        // Also when adding into an existing party
        Assert.Equal(HttpStatusCode.Conflict, (await Add(c, existing.Id, ("jose", u))).StatusCode);
    }

    [Fact]
    public async Task Unknown_party_is_404()
    {
        var res = await Add(await Admin(), "no-such-party", ("A", Unique()));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Blank_name_anywhere_in_batch_saves_nothing()
    {
        var c = await Admin();
        var u = Unique();
        var res = await Add(c, null, ("Good", u), ("", u));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.DoesNotContain((await List(c)).SelectMany(p => p.Guests), g => g.LastName == u);
    }

    [Fact]
    public async Task Listing_returns_every_party_with_guest_shape()
    {
        var c = await Admin();
        var u = Unique();
        var a = await Body(await Add(c, null, ("A", u)));
        var b = await Body(await Add(c, null, ("B", u), ("C", u)));

        var json = await (await c.GetAsync("/admin/parties")).Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var guest = doc.RootElement.EnumerateArray().First().GetProperty("guests")[0];
        Assert.Equal(
            ["firstName", "id", "lastName", "rsvp", "rsvpChangedAt", "rsvpChangedBy"],
            guest.EnumerateObject().Select(p => p.Name).Order().ToArray());

        var parties = await List(c);
        Assert.Equal(1, parties.Count(p => p.Id == a.Id));
        Assert.Equal(2, parties.Single(p => p.Id == b.Id).Guests.Count);
    }

    [Fact]
    public async Task Old_code_based_endpoints_are_gone()
    {
        var c = await Admin();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/party/ABC")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/rsvp/ABC", new { })).StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;

namespace WeddingApi.Tests;

public class LookupTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    record AddRequest(string? PartyId, GuestName[] Guests);
    record LookupBody(string? FirstName, string? LastName);
    record LookupResponse(PartyDto Party);

    static string Unique() => Guid.NewGuid().ToString("N");

    async Task<PartyDto> Seed(params (string First, string Last)[] guests)
    {
        var admin = await api.SignedInClientAsync(ApiFactory.AdminPassword);
        var res = await admin.PostAsJsonAsync("/admin/guests", new AddRequest(null, guests.Select(g => new GuestName(g.First, g.Last)).ToArray()));
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PartyDto>())!;
    }

    async Task<HttpResponseMessage> Lookup(string? first, string? last)
    {
        var c = await api.SignedInClientAsync(ApiFactory.EventPassword);
        return await c.PostAsJsonAsync("/lookup", new LookupBody(first, last));
    }

    [Fact]
    public async Task Exact_name_returns_whole_party()
    {
        var u = Unique();
        var seeded = await Seed(("Andrew", u), ("Sam", u));
        var res = await Lookup("Andrew", u);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var party = (await res.Content.ReadFromJsonAsync<LookupResponse>())!.Party;
        Assert.Equal(seeded.Id, party.Id);
        Assert.Equal(["Andrew", "Sam"], party.Guests.Select(g => g.FirstName));
        Assert.All(party.Guests, g => Assert.Equal("pending", g.Rsvp));
    }

    [Fact]
    public async Task Casing_and_whitespace_are_ignored()
    {
        var u = Unique();
        await Seed(("Andrew", u));
        Assert.Equal(HttpStatusCode.OK, (await Lookup("  ANDREW ", u.ToUpperInvariant() + " ")).StatusCode);
    }

    [Fact]
    public async Task Accents_are_ignored_both_ways()
    {
        var u = Unique();
        await Seed(("José", "Peña" + u));
        Assert.Equal(HttpStatusCode.OK, (await Lookup("Jose", "Pena" + u)).StatusCode);
        var u2 = Unique();
        await Seed(("Jose", "Pena" + u2));
        Assert.Equal(HttpStatusCode.OK, (await Lookup("José", "Peña" + u2)).StatusCode);
    }

    [Fact]
    public async Task Partial_blank_and_unknown_names_are_404()
    {
        var u = Unique();
        await Seed(("Andrew", u));
        Assert.Equal(HttpStatusCode.NotFound, (await Lookup("Andrew", "")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Lookup("Andr", u)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Lookup("Nobody", Unique())).StatusCode);
    }

    [Fact]
    public async Task Requires_a_token()
    {
        var res = await api.CreateClient().PostAsJsonAsync("/lookup", new LookupBody("A", "B"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}

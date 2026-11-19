using System.Net;
using System.Net.Http.Json;

namespace WeddingApi.Tests;

public class AdminRsvpTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    record AddRequest(string? PartyId, GuestName[] Guests);
    record Body(string Rsvp);

    static string Unique() => Guid.NewGuid().ToString("N");

    async Task<PartyDto> Seed()
    {
        var admin = await api.SignedInClientAsync(ApiFactory.AdminPassword);
        var u = Unique();
        var res = await admin.PostAsJsonAsync("/admin/guests", new AddRequest(null, [new GuestName("A", u), new GuestName("B", u)]));
        return (await res.Content.ReadFromJsonAsync<PartyDto>())!;
    }

    async Task<HttpResponseMessage> Put(string guestId, string rsvp) =>
        await (await api.SignedInClientAsync(ApiFactory.AdminPassword)).PutAsJsonAsync($"/admin/guests/{guestId}/rsvp", new Body(rsvp));

    [Theory]
    [InlineData("attending")]
    [InlineData("declined")]
    [InlineData("pending")]
    public async Task Admin_can_set_each_status_and_it_is_stamped(string status)
    {
        var p = await Seed();
        // Start from a different status so "pending" is a real reset
        await Put(p.Guests[0].Id, status == "attending" ? "declined" : "attending");

        var res = await Put(p.Guests[0].Id, status);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var party = (await res.Content.ReadFromJsonAsync<PartyDto>())!;
        Assert.Equal(p.Id, party.Id);
        Assert.Equal(status, party.Guests[0].Rsvp);
        Assert.Equal("admin", party.Guests[0].RsvpChangedBy);
        Assert.Equal(api.Clock.GetUtcNow().UtcDateTime, party.Guests[0].RsvpChangedAt);
        Assert.Equal("pending", party.Guests[1].Rsvp);
        Assert.Null(party.Guests[1].RsvpChangedBy);
    }

    [Fact]
    public async Task Unknown_guest_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Put(Unique(), "attending")).StatusCode);
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("")]
    public async Task Unknown_status_is_400(string status)
    {
        var p = await Seed();
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(p.Guests[0].Id, status)).StatusCode);
    }

    [Fact]
    public async Task Guest_tokens_are_rejected()
    {
        var p = await Seed();
        var guest = await api.SignedInClientAsync(ApiFactory.EventPassword);
        var res = await guest.PutAsJsonAsync($"/admin/guests/{p.Guests[0].Id}/rsvp", new Body("attending"));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;

namespace WeddingApi.Tests;

public class PartyRsvpTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    record AddRequest(string? PartyId, GuestName[] Guests);
    record Answer(string GuestId, string Rsvp);
    record Body(Answer[] Answers);

    static string Unique() => Guid.NewGuid().ToString("N");

    async Task<PartyDto> Seed(int count)
    {
        var admin = await api.SignedInClientAsync(ApiFactory.AdminPassword);
        var u = Unique();
        var res = await admin.PostAsJsonAsync("/admin/guests", new AddRequest(null, Enumerable.Range(0, count).Select(i => new GuestName("G" + i, u)).ToArray()));
        return (await res.Content.ReadFromJsonAsync<PartyDto>())!;
    }

    async Task<PartyDto> Reload(string id)
    {
        var admin = await api.SignedInClientAsync(ApiFactory.AdminPassword);
        return (await admin.GetFromJsonAsync<List<PartyDto>>("/admin/parties"))!.Single(p => p.Id == id);
    }

    async Task<HttpResponseMessage> Post(string partyId, params Answer[] answers) =>
        await (await api.SignedInClientAsync(ApiFactory.EventPassword)).PostAsJsonAsync($"/parties/{partyId}/rsvp", new Body(answers));

    [Fact]
    public async Task Partial_answers_update_only_those_guests_and_stamp_them()
    {
        var p = await Seed(3);
        var res = await Post(p.Id, new Answer(p.Guests[0].Id, "attending"), new Answer(p.Guests[1].Id, "declined"));
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var after = (await Reload(p.Id)).Guests;
        Assert.Equal(["attending", "declined", "pending"], after.Select(g => g.Rsvp));
        Assert.All(after.Take(2), g =>
        {
            Assert.Equal("guest", g.RsvpChangedBy);
            Assert.Equal(api.Clock.GetUtcNow().UtcDateTime, g.RsvpChangedAt);
        });
        Assert.Null(after[2].RsvpChangedAt);
        Assert.Null(after[2].RsvpChangedBy);
    }

    [Fact]
    public async Task An_existing_answer_can_be_changed()
    {
        var p = await Seed(1);
        await Post(p.Id, new Answer(p.Guests[0].Id, "attending"));
        Assert.Equal(HttpStatusCode.NoContent, (await Post(p.Id, new Answer(p.Guests[0].Id, "declined"))).StatusCode);
        Assert.Equal("declined", (await Reload(p.Id)).Guests[0].Rsvp);
    }

    [Fact]
    public async Task Guest_from_another_party_rejects_everything()
    {
        var p = await Seed(1);
        var other = await Seed(1);
        var res = await Post(p.Id, new Answer(p.Guests[0].Id, "attending"), new Answer(other.Guests[0].Id, "attending"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("pending", (await Reload(p.Id)).Guests[0].Rsvp);
        Assert.Equal("pending", (await Reload(other.Id)).Guests[0].Rsvp);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("maybe")]
    [InlineData("")]
    public async Task Disallowed_values_are_400(string value)
    {
        var p = await Seed(1);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(p.Id, new Answer(p.Guests[0].Id, value))).StatusCode);
    }

    [Fact]
    public async Task Empty_answers_are_400()
    {
        var p = await Seed(1);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(p.Id)).StatusCode);
    }

    [Fact]
    public async Task Unknown_party_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Post(Unique(), new Answer("x", "attending"))).StatusCode);
    }

    [Fact]
    public async Task Requires_a_token()
    {
        var res = await api.CreateClient().PostAsJsonAsync("/parties/x/rsvp", new Body([new Answer("x", "attending")]));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}

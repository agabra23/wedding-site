using System.Net;
using System.Net.Http.Json;

namespace WeddingApi.Tests;

public class RsvpDeadlineTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    record AddRequest(string? PartyId, GuestName[] Guests);
    record Answer(string GuestId, string Rsvp);
    record RsvpBody(Answer[] Answers);
    record LookupBody(string FirstName, string LastName);
    record LookupResponse(PartyDto Party, DateTime? RsvpDeadline);
    record SettingsBody(string? RsvpDeadline);
    record SettingsResponse(DateTime? RsvpDeadline);

    static string Unique() => Guid.NewGuid().ToString("N");

    Task<HttpClient> Admin() => api.SignedInClientAsync(ApiFactory.AdminPassword);
    Task<HttpClient> Guest() => api.SignedInClientAsync(ApiFactory.EventPassword);

    async Task SetDeadline(string? value) =>
        (await (await Admin()).PutAsJsonAsync("/admin/settings", new SettingsBody(value))).EnsureSuccessStatusCode();

    async Task<(PartyDto Party, string Last)> Seed()
    {
        var last = Unique();
        var res = await (await Admin()).PostAsJsonAsync("/admin/guests", new AddRequest(null, [new GuestName("Ann", last)]));
        return ((await res.Content.ReadFromJsonAsync<PartyDto>())!, last);
    }

    async Task<HttpResponseMessage> Rsvp(PartyDto p, string rsvp = "attending") =>
        await (await Guest()).PostAsJsonAsync($"/parties/{p.Id}/rsvp", new RsvpBody([new Answer(p.Guests[0].Id, rsvp)]));

    [Fact]
    public async Task Rsvps_are_accepted_when_no_deadline_is_set()
    {
        await SetDeadline(null);
        var (p, _) = await Seed();
        Assert.Equal(HttpStatusCode.NoContent, (await Rsvp(p)).StatusCode);
    }

    [Fact]
    public async Task Admin_can_set_change_clear_and_read_back_the_deadline()
    {
        var admin = await Admin();
        await SetDeadline("2027-06-01T12:00:00Z");
        Assert.Equal(new DateTime(2027, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            (await admin.GetFromJsonAsync<SettingsResponse>("/admin/settings"))!.RsvpDeadline);

        // An offset is converted to the exact same moment
        await SetDeadline("2027-07-01T12:00:00-04:00");
        Assert.Equal(new DateTime(2027, 7, 1, 16, 0, 0, DateTimeKind.Utc),
            (await admin.GetFromJsonAsync<SettingsResponse>("/admin/settings"))!.RsvpDeadline);

        await SetDeadline(null);
        Assert.Null((await admin.GetFromJsonAsync<SettingsResponse>("/admin/settings"))!.RsvpDeadline);
    }

    [Fact]
    public async Task Unreadable_time_is_a_bad_request_and_changes_nothing()
    {
        await SetDeadline("2027-06-01T12:00:00Z");
        var res = await (await Admin()).PutAsJsonAsync("/admin/settings", new SettingsBody("next friday-ish"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.NotNull((await (await Admin()).GetFromJsonAsync<SettingsResponse>("/admin/settings"))!.RsvpDeadline);
    }

    [Fact]
    public async Task Rsvps_work_before_the_deadline_and_are_refused_after_it()
    {
        var (p, _) = await Seed();
        await SetDeadline(api.Clock.GetUtcNow().AddDays(1).ToString("O"));
        Assert.Equal(HttpStatusCode.NoContent, (await Rsvp(p)).StatusCode);

        var before = api.Clock.GetUtcNow();
        api.Clock.Advance(TimeSpan.FromDays(2));
        try
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Rsvp(p, "declined")).StatusCode);
            var after = (await (await Admin()).GetFromJsonAsync<List<PartyDto>>("/admin/parties"))!.Single(x => x.Id == p.Id);
            Assert.Equal("attending", after.Guests[0].Rsvp);
        }
        finally
        {
            api.Clock.SetUtcNow(before);
        }
    }

    [Fact]
    public async Task Lookup_still_works_after_the_deadline_and_returns_it()
    {
        var (p, last) = await Seed();
        var deadline = api.Clock.GetUtcNow().AddDays(-1);
        await SetDeadline(deadline.ToString("O"));

        var res = await (await Guest()).PostAsJsonAsync("/lookup", new LookupBody("Ann", last));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = (await res.Content.ReadFromJsonAsync<LookupResponse>())!;
        Assert.Equal(p.Id, body.Party.Id);
        Assert.Equal(deadline.UtcDateTime, body.RsvpDeadline);
    }

    [Fact]
    public async Task Guest_tokens_cannot_use_the_settings_endpoints()
    {
        var guest = await Guest();
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync("/admin/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await guest.PutAsJsonAsync("/admin/settings", new SettingsBody(null))).StatusCode);
    }
}

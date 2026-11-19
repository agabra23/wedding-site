using System.Net;
using System.Net.Http.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;

namespace WeddingApi.Tests;

public class AuthTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    static async Task<HttpResponseMessage> SignIn(HttpClient c, string? password) =>
        await c.PostAsJsonAsync("/auth", new AuthRequest(password));

    [Fact]
    public async Task Event_password_returns_guest_token()
    {
        var res = await SignIn(api.CreateClient(), ApiFactory.EventPassword);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.Equal("guest", body!.Role);
        Assert.False(string.IsNullOrEmpty(body.Token));
    }

    [Fact]
    public async Task Admin_password_returns_admin_token()
    {
        var res = await SignIn(api.CreateClient(), ApiFactory.AdminPassword);
        Assert.Equal("admin", (await res.Content.ReadFromJsonAsync<AuthResponse>())!.Role);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Other_passwords_are_rejected(string? password)
    {
        var res = await SignIn(api.CreateClient(), password);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Health_and_auth_need_no_token()
    {
        var c = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health")).StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await SignIn(c, ApiFactory.EventPassword)).StatusCode);
    }

    [Fact]
    public async Task Admin_endpoints_return_401_without_a_token()
    {
        var c = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/admin/parties")).StatusCode);
        var post = await c.PostAsJsonAsync("/admin/guests", new AddGuestsRequest(null, []));
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [Fact]
    public async Task Garbage_token_returns_401()
    {
        var c = ApiFactory.WithToken(api.CreateClient(), "not-a-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/admin/parties")).StatusCode);
    }

    [Fact]
    public async Task Admin_endpoints_return_403_for_a_guest()
    {
        var c = await api.SignedInClientAsync(ApiFactory.EventPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/admin/parties")).StatusCode);
        var post = await c.PostAsJsonAsync("/admin/guests", new AddGuestsRequest(null, [new("A", "B")]));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [Fact]
    public async Task Admin_token_can_call_admin_endpoints()
    {
        var c = await api.SignedInClientAsync(ApiFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/admin/parties")).StatusCode);
    }

    [Fact]
    public async Task Guest_token_expires_after_30_days()
    {
        // The fake clock only moves forward and is shared by the class, so measure from now
        var start = api.Clock.GetUtcNow();
        var c = await api.SignedInClientAsync(ApiFactory.EventPassword);

        api.Clock.SetUtcNow(start + TimeSpan.FromDays(30) - TimeSpan.FromSeconds(1));
        // A valid guest token is authenticated but not an Admin
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/admin/parties")).StatusCode);

        api.Clock.SetUtcNow(start + TimeSpan.FromDays(30));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/admin/parties")).StatusCode);
    }

    [Fact]
    public async Task Admin_token_expires_after_24_hours()
    {
        // The fake clock only moves forward and is shared by the class, so measure from now
        var start = api.Clock.GetUtcNow();
        var c = await api.SignedInClientAsync(ApiFactory.AdminPassword);

        api.Clock.SetUtcNow(start + TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/admin/parties")).StatusCode);

        api.Clock.SetUtcNow(start + TimeSpan.FromHours(24));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/admin/parties")).StatusCode);
    }
}

public class AuthRateLimitTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Too_many_auth_attempts_from_one_ip_are_rate_limited()
    {
        using var limited = api.WithWebHostBuilder(b => b.UseSetting("AuthRequestsPerMinute", "5"));
        var c = limited.CreateClient();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await c.PostAsJsonAsync("/auth", new AuthRequest("wrong"))).StatusCode);

        // Even the right password is refused once the limit is hit
        var res = await c.PostAsJsonAsync("/auth", new AuthRequest(ApiFactory.EventPassword));
        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
    }
}

public class LogoutTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(ApiFactory.EventPassword, HttpStatusCode.Forbidden)]
    [InlineData(ApiFactory.AdminPassword, HttpStatusCode.OK)]
    public async Task Token_works_before_logout_and_returns_401_after(string password, HttpStatusCode before)
    {
        var c = await api.SignedInClientAsync(password);
        Assert.Equal(before, (await c.GetAsync("/admin/parties")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/auth/logout", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/admin/parties")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/lookup", new LookupRequest("x", "y"))).StatusCode);
        // Logging out again is like any other call with a revoked token
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync("/auth/logout", null)).StatusCode);
    }

    [Fact]
    public async Task Logging_out_one_token_leaves_other_tokens_for_the_same_role_working()
    {
        var first = await api.SignedInClientAsync(ApiFactory.AdminPassword);
        var second = await api.SignedInClientAsync(ApiFactory.AdminPassword);

        await first.PostAsync("/auth/logout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/admin/parties")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/admin/parties")).StatusCode);
    }

    [Fact]
    public async Task Logout_without_a_token_returns_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().PostAsync("/auth/logout", null)).StatusCode);
    }

    [Fact]
    public async Task Revocation_document_expires_with_the_token()
    {
        var token = await api.TokenAsync(ApiFactory.AdminPassword);
        var jti = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(token).Id;
        var before = api.Clock.GetUtcNow();
        await ApiFactory.WithToken(api.CreateClient(), token).PostAsync("/auth/logout", null);

        var container = api.Services.GetRequiredService<CosmosClient>().GetContainer(api.Database, ApiFactory.Container);
        var doc = (await container.ReadItemAsync<Revocation>(Revocation.IdPrefix + jti, new PartitionKey(Partitioning.Value))).Resource;

        Assert.Equal(before.ToUnixTimeSeconds() + (long)AuthSettings.AdminLifetime.TotalSeconds, doc.Exp);
        Assert.Equal((int)AuthSettings.AdminLifetime.TotalSeconds, doc.Ttl);
    }

    [Fact]
    public async Task Auth_still_issues_working_tokens_after_a_logout()
    {
        await (await api.SignedInClientAsync(ApiFactory.AdminPassword)).PostAsync("/auth/logout", null);
        var c = await api.SignedInClientAsync(ApiFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/admin/parties")).StatusCode);
    }
}

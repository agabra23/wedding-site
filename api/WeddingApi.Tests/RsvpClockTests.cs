using System.Net;
using System.Net.Http.Json;
using Microsoft.Azure.Cosmos;

namespace WeddingApi.Tests;

public class RsvpClockTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Rsvp_is_stamped_with_the_injected_clock()
    {
        var now = new DateTimeOffset(2027, 3, 4, 5, 6, 7, TimeSpan.Zero);
        api.Clock.SetUtcNow(now);

        using var cosmos = new CosmosClient(
            Environment.GetEnvironmentVariable("COSMOS_TEST_CONNECTION") ?? ApiFactory.DefaultConnection,
            new CosmosClientOptions
            {
                ConnectionMode = ConnectionMode.Gateway,
                SerializerOptions = new() { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase },
            });
        var guest = new Guest("Ada", "pending", null, false);
        await cosmos.GetContainer(api.Database, ApiFactory.Container)
            .CreateItemAsync(new Party("ABC", "Ada's party", null, [guest], null));

        var client = api.CreateClient();
        var post = await client.PostAsJsonAsync("/rsvp/abc",
            new RsvpRequest(null, [new GuestAnswer("Ada", "yes", null)]));
        Assert.Equal(HttpStatusCode.NoContent, post.StatusCode);

        var party = await client.GetFromJsonAsync<Party>("/party/ABC",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(now.UtcDateTime, party!.RespondedAt);
    }
}

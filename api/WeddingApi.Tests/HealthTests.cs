using System.Net;

namespace WeddingApi.Tests;

public class HealthTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_returns_ok()
    {
        var response = await api.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

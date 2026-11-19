using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace WeddingApi.Tests;

/// <summary>
/// Hosts the API in-process against the Cosmos emulator. Each instance creates its own
/// database (with the single-partition container) and deletes it on dispose, so create one
/// per test class (xunit IClassFixture) or per test.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // The emulator's well-known key (not a secret)
    const string EmulatorKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    public const string DefaultConnection =
        $"AccountEndpoint=http://localhost:8081/;AccountKey={EmulatorKey};";

    readonly string connection =
        Environment.GetEnvironmentVariable("COSMOS_TEST_CONNECTION") ?? DefaultConnection;

    public string Database { get; } = "test-" + Guid.NewGuid().ToString("N");
    public const string Container = "parties";

    /// <summary>Swap-in clock: call SetUtcNow / Advance to control the API's current time.</summary>
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("CosmosConnection", connection);
        builder.UseSetting("CosmosGatewayMode", "true");
        builder.UseSetting("CosmosDatabase", Database);
        builder.UseSetting("PartiesContainer", Container);
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock)));
    }

    CosmosClient NewAdminClient() => new(connection, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });

    async Task IAsyncLifetime.InitializeAsync()
    {
        using var client = NewAdminClient();
        var db = (await client.CreateDatabaseAsync(Database)).Database;
        await db.CreateContainerAsync(Container, Partitioning.Path);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using var client = NewAdminClient();
        await client.GetDatabase(Database).DeleteAsync();
        await base.DisposeAsync();
    }
}

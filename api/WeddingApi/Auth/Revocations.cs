using System.Net;
using Microsoft.Azure.Cosmos;

/// <summary>
/// One document per revoked token id, in the Parties container and partition (ADR-0001).
/// Cosmos deletes it by TTL once the token would have expired anyway.
/// </summary>
public record Revocation(string Id, long Exp, int Ttl)
{
    public const string IdPrefix = "revoked:";
    public string Pk { get; init; } = Partitioning.Value;
}

public static class Revocations
{
    static string DocumentId(string jti) => Revocation.IdPrefix + jti;

    public static async Task Revoke(Container container, string jti, long exp, TimeProvider clock)
    {
        var secondsLeft = exp - clock.GetUtcNow().ToUnixTimeSeconds();
        var ttl = (int)Math.Max(1, secondsLeft);
        await container.UpsertItemAsync(new Revocation(DocumentId(jti), exp, ttl), new PartitionKey(Partitioning.Value));
    }

    public static async Task<bool> IsRevoked(Container container, string jti)
    {
        try
        {
            await container.ReadItemAsync<Revocation>(DocumentId(jti), new PartitionKey(Partitioning.Value));
            return true;
        }
        catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}

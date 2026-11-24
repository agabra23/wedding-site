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

    public static async Task Revoke(Database db, string jti, long exp, TimeProvider clock)
    {
        var secondsLeft = exp - clock.GetUtcNow().ToUnixTimeSeconds();
        var ttl = (int)Math.Max(1, secondsLeft);
        await db.Container.UpsertItemAsync(new Revocation(DocumentId(jti), exp, ttl), Database.Key);
    }

    public static async Task<bool> IsRevoked(Database db, string jti)
    {
        try
        {
            await db.Container.ReadItemAsync<Revocation>(DocumentId(jti), Database.Key);
            return true;
        }
        catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}

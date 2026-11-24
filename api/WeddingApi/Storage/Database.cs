using Microsoft.Azure.Cosmos;

/// <summary>
/// The Parties container and its one partition key (ADR-0001). Everything that touches Cosmos goes through here,
/// so no other code builds a <see cref="PartitionKey"/> or looks a container up.
/// </summary>
public sealed class Database(CosmosClient cosmos, string database, string container)
{
    public Container Container { get; } = cosmos.GetContainer(database, container);

    public static PartitionKey Key { get; } = new(Partitioning.Value);

    public static Database From(CosmosClient cosmos, IConfiguration config) =>
        new(cosmos, config["CosmosDatabase"] ?? "wedding", config["PartiesContainer"] ?? "parties");
}

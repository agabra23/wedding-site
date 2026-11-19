using Microsoft.Azure.Cosmos;

public record LookupRequest(string? FirstName, string? LastName);

public static class GuestLookup
{
    public static void MapLookup(this WebApplication app)
    {
        var pk = new PartitionKey(Partitioning.Value);

        app.MapPost("/lookup", async (LookupRequest req, CosmosClient cosmos) =>
        {
            if (string.IsNullOrWhiteSpace(req.FirstName) || string.IsNullOrWhiteSpace(req.LastName))
                return Results.NotFound();

            var first = Names.Normalize(req.FirstName);
            var last = Names.Normalize(req.LastName);

            var container = cosmos.GetContainer(app.Configuration["CosmosDatabase"] ?? "wedding", app.Configuration["PartiesContainer"] ?? "parties");
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE EXISTS(SELECT VALUE g FROM g IN c.guests WHERE g.firstNameNormalized = @f AND g.lastNameNormalized = @l)")
                .WithParameter("@f", first).WithParameter("@l", last);
            var it = container.GetItemQueryIterator<Party>(query, requestOptions: new QueryRequestOptions { PartitionKey = pk });
            while (it.HasMoreResults)
                foreach (var party in await it.ReadNextAsync())
                    return Results.Ok(new { party = PartyDto.From(party) });
            return Results.NotFound();
        }).RequireAuthorization(Policies.Guest);
    }
}

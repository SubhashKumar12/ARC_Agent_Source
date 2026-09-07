using Microsoft.Azure.Cosmos;
using ARC.Data.Configuration;
using ARC.Data.Cosmos;

namespace ARC.Integration.Tests.Fixtures;

public sealed class CosmosFixture
{
    public string ConnectionString { get; } = InfrastructureGate.ResolveCosmosConnectionString();
    public string DatabaseId { get; } = "arc";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var client = InfrastructureGate.CreateCosmosClient(ConnectionString);
        var database = (await client.CreateDatabaseIfNotExistsAsync(DatabaseId, cancellationToken: cancellationToken)).Database;

        foreach (var container in new[]
                 {
                     ("checkpoints", "/cycleId"),
                     ("cycleState", "/cycleId"),
                     ("auditEvents", "/cycleId"),
                     ("conversationState", "/cycleId")
                 })
        {
            await database.CreateContainerIfNotExistsAsync(
                new ContainerProperties(container.Item1, container.Item2),
                cancellationToken: cancellationToken);
        }

        try
        {
            await database.CreateContainerIfNotExistsAsync(
                CosmosDocumentsContract.CreateContainerProperties("documents"),
                cancellationToken: cancellationToken);
        }
        catch (Exception)
        {
            await database.CreateContainerIfNotExistsAsync(
                CosmosDocumentsContract.CreateContainerProperties("documents", includeVectorIndex: false),
                cancellationToken: cancellationToken);
        }
    }

    public async Task<int> CountMafCheckpointsAsync(string sessionId, CancellationToken cancellationToken)
    {
        using var client = InfrastructureGate.CreateCosmosClient(ConnectionString);
        var container = client.GetContainer(DatabaseId, "checkpoints");
        var cycleId = sessionId.Split('|')[0];
        var query = new QueryDefinition(
                "SELECT VALUE COUNT(1) FROM c WHERE c.docType = @docType AND c.sessionId = @sessionId")
            .WithParameter("@docType", "maf")
            .WithParameter("@sessionId", sessionId);

        using var iterator = container.GetItemQueryIterator<int>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(cycleId) });
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var value in page)
                return value;
        }

        return 0;
    }

    public CosmosStoreOptions ToOptions() => new()
    {
        ConnectionString = ConnectionString,
        UseManagedIdentity = false,
        DatabaseId = DatabaseId,
        CheckpointsContainer = "checkpoints",
        CycleStateContainer = "cycleState",
        AuditContainer = "auditEvents",
        ConversationContainer = "conversationState",
        DocumentsContainer = "documents"
    };
}

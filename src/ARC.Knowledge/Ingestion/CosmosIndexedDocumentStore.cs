using ARC.Data.Cosmos;
using ARC.Knowledge.Vector;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace ARC.Knowledge.Ingestion;

/// <summary>
/// Cosmos DB implementation of IIndexedDocumentStore.
/// Uses documents container with /documentType partition key.
/// </summary>
public sealed class CosmosIndexedDocumentStore : IIndexedDocumentStore
{
    private readonly Container _container;

    public CosmosIndexedDocumentStore(CosmosClient cosmosClient, string databaseId)
    {
        var database = cosmosClient.GetDatabase(databaseId);
        _container = database.GetContainer(CosmosDocumentsContract.ContainerName);
    }

    public async Task<IndexedDocument?> FindByContentHashAsync(
        string contentHash,
        string embeddingModel,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentHash) || string.IsNullOrWhiteSpace(embeddingModel))
            return null;

        try
        {
            // Query for document with matching contentHash and embeddingModel
            var query = _container.GetItemLinqQueryable<IndexedDocument>()
                .Where(d => d.contentHash == contentHash && d.embeddingModel == embeddingModel)
                .Take(1);

            using var iterator = query.ToFeedIterator();
            
            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                return response.FirstOrDefault();
            }

            return null;
        }
        catch (CosmosException)
        {
            return null;
        }
    }

    public async Task UpsertAsync(
        IndexedDocument document,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        if (string.IsNullOrWhiteSpace(document.id))
            throw new ArgumentException("Document id is required.", nameof(document));

        if (string.IsNullOrWhiteSpace(document.documentType))
            throw new ArgumentException("Document documentType (partition key) is required.", nameof(document));

        var partitionKey = new PartitionKey(document.documentType);
        
        await _container.UpsertItemAsync(
            document,
            partitionKey,
            cancellationToken: cancellationToken);
    }

    public async Task UpsertBatchAsync(
        IEnumerable<IndexedDocument> documents,
        CancellationToken cancellationToken = default)
    {
        if (documents == null)
            throw new ArgumentNullException(nameof(documents));

        var documentList = documents.ToList();
        if (documentList.Count == 0)
            return;

        // Group by partition key (documentType) for transactional batch
        var grouped = documentList.GroupBy(d => d.documentType);

        foreach (var group in grouped)
        {
            var partitionKey = new PartitionKey(group.Key);
            var batch = _container.CreateTransactionalBatch(partitionKey);

            foreach (var document in group)
            {
                if (string.IsNullOrWhiteSpace(document.id))
                    throw new ArgumentException("Document id is required for batch upsert.");

                batch.UpsertItem(document);
            }

            var response = await batch.ExecuteAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Batch upsert failed with status {response.StatusCode}: {response.ErrorMessage}");
            }
        }
    }

    public async Task<IReadOnlyList<IndexedDocument>> QueryByDocumentTypeAsync(
        string documentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentType))
            return Array.Empty<IndexedDocument>();

        var query = _container.GetItemLinqQueryable<IndexedDocument>(
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(documentType) })
            .Where(d => d.documentType == documentType);

        using var iterator = query.ToFeedIterator();
        var results = new List<IndexedDocument>();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    public async Task<IReadOnlyList<IndexedDocument>> QueryBySourceAndVersionAsync(
        string documentType,
        string sourceDocumentId,
        string version,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentType)
            || string.IsNullOrWhiteSpace(sourceDocumentId)
            || string.IsNullOrWhiteSpace(version))
            return Array.Empty<IndexedDocument>();

        var query = _container.GetItemLinqQueryable<IndexedDocument>(
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(documentType) })
            .Where(d =>
                d.documentType == documentType
                && d.sourceDocumentId == sourceDocumentId
                && d.version == version);

        using var iterator = query.ToFeedIterator();
        var results = new List<IndexedDocument>();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }
}

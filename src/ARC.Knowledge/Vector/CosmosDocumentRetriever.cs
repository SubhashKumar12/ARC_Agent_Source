using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using ARC.Data.Cosmos;
using ARC.Knowledge.Exceptions;
using ARC.Knowledge.Provenance;
using ARC.Knowledge.Retrieval;

namespace ARC.Knowledge.Vector;

/// <summary>
/// Cosmos documents container: VectorDistance when an embedding is supplied; CONTAINS otherwise.
/// </summary>
public sealed class CosmosDocumentRetriever : IDocumentRetriever
{
    private readonly Container _documents;
    private readonly ILogger<CosmosDocumentRetriever> _logger;

    public CosmosDocumentRetriever(ICosmosClientFactory cosmos, ILogger<CosmosDocumentRetriever> logger)
    {
        _documents = cosmos.Documents;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EvidenceSource>> RetrieveAsync(
        DocumentRetrievalRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var definition = CosmosDocumentQuery.Build(request);
            var options = QueryOptions(request);
            var results = new List<EvidenceSource>();
            using var iterator = _documents.GetItemQueryIterator<IndexedDocument>(definition, requestOptions: options);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                foreach (var doc in page)
                    results.Add(ToSource(doc, request.TopK));
            }

            _logger.LogInformation(
                "Document retrieval kind {Kind} returned {Count} items topK {TopK}",
                request.Kind,
                results.Count,
                request.TopK);

            return results.Take(request.TopK).ToList();
        }
        catch (Exception ex)
        {
            throw new RetrievalFailedException("Knowledge retrieval against Cosmos documents failed.", ex);
        }
    }

    private static QueryRequestOptions QueryOptions(DocumentRetrievalRequest request)
    {
        var options = new QueryRequestOptions { MaxItemCount = request.TopK };
        if (!string.IsNullOrWhiteSpace(request.Filter.DocumentType))
            options.PartitionKey = new PartitionKey(request.Filter.DocumentType);
        return options;
    }

    private static EvidenceSource ToSource(IndexedDocument doc, int unusedTopK)
    {
        _ = unusedTopK;
        var content = doc.content ?? "";
        return new EvidenceSource(
            new SourceReference(
                doc.id,
                doc.blobLocation,
                null,
                doc.version,
                doc.pageOrSection,
                "cosmos-documents",
                DateTimeOffset.UtcNow),
            string.IsNullOrWhiteSpace(doc.title) ? doc.id : doc.title,
            content,
            doc.similarityScore,
            doc.status,
            RegionScope: JoinScope(doc.regionScope),
            DealerUrn: doc.dealerUrn,
            SourceDocumentId: doc.sourceDocumentId,
            DocumentType: doc.documentType);
    }

    private static string? JoinScope(string[]? scope)
        => scope is { Length: > 0 } ? string.Join(',', scope) : null;
}

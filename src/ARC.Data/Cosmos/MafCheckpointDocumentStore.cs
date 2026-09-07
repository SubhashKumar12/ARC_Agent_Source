using Microsoft.Azure.Cosmos;
using ARC.Data.Exceptions;

namespace ARC.Data.Cosmos;

public sealed class MafCheckpointDocumentStore : IMafCheckpointDocumentStore
{
    private readonly ICosmosClientFactory _cosmos;

    public MafCheckpointDocumentStore(ICosmosClientFactory cosmos) => _cosmos = cosmos;

    public async Task UpsertAsync(MafCheckpointDocument document, CancellationToken cancellationToken)
    {
        var row = Row.From(document);
        try
        {
            await _cosmos.Checkpoints.UpsertItemAsync(row, new PartitionKey(row.cycleId), cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to save MAF checkpoint.", ex);
        }
    }

    public async Task<MafCheckpointDocument?> GetAsync(string sessionId, string checkpointId, CancellationToken cancellationToken)
    {
        var cycleId = CycleFromSession(sessionId);
        try
        {
            var response = await _cosmos.Checkpoints.ReadItemAsync<Row>(
                Row.IdFor(sessionId, checkpointId),
                new PartitionKey(cycleId),
                cancellationToken: cancellationToken);
            return response.Resource.ToDomain();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to load MAF checkpoint.", ex);
        }
    }

    public async Task<IReadOnlyList<MafCheckpointDocument>> ListBySessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var cycleId = CycleFromSession(sessionId);
        // Single-property ORDER BY uses the default range index. A second ORDER BY
        // column would require a composite index the checkpoints container does not have.
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.docType = @docType AND c.sessionId = @sessionId ORDER BY c.committedUtc ASC")
            .WithParameter("@docType", Row.DocType)
            .WithParameter("@sessionId", sessionId);

        try
        {
            var results = new List<MafCheckpointDocument>();
            using var iterator = _cosmos.Checkpoints.GetItemQueryIterator<Row>(
                query,
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(cycleId) });
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(page.Resource.Select(r => r.ToDomain()));
            }

            // MAF RetrieveIndexAsync contract: oldest commit first, latest last.
            // checkpointId is a stable same-tick tie-break only (not applied in Cosmos SQL).
            return results
                .OrderBy(d => d.CommittedUtc)
                .ThenBy(d => d.CheckpointId, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to list MAF checkpoints.", ex);
        }
    }

    public static string CycleFromSession(string sessionId)
    {
        var separator = sessionId.IndexOf('|');
        return separator > 0 ? sessionId[..separator] : sessionId;
    }

    private sealed class Row
    {
        public const string DocType = "maf";

        public string id { get; set; } = "";
        public string docType { get; set; } = DocType;
        public string cycleId { get; set; } = "";
        public string sessionId { get; set; } = "";
        public string checkpointId { get; set; } = "";
        public string? parentCheckpointId { get; set; }
        public DateTimeOffset committedUtc { get; set; }
        public string payloadJson { get; set; } = "";

        public static string IdFor(string sessionId, string checkpointId) => $"maf:{sessionId}:{checkpointId}";

        public static Row From(MafCheckpointDocument document) => new()
        {
            id = IdFor(document.SessionId, document.CheckpointId),
            docType = DocType,
            cycleId = document.CycleId,
            sessionId = document.SessionId,
            checkpointId = document.CheckpointId,
            parentCheckpointId = document.ParentCheckpointId,
            committedUtc = document.CommittedUtc,
            payloadJson = document.PayloadJson
        };

        public MafCheckpointDocument ToDomain() => new(
            sessionId,
            checkpointId,
            cycleId,
            parentCheckpointId,
            committedUtc,
            payloadJson);
    }
}

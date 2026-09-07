using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using ARC.Data.Cosmos;

namespace ARC.Host.Functions.Checkpointing;

/// <summary>Cosmos-backed MAF JSON checkpoint store. In-memory is not sufficient for 15+ day gates.</summary>
public sealed class CosmosJsonCheckpointStore : ICheckpointStore<JsonElement>
{
    private readonly IMafCheckpointDocumentStore _store;

    public CosmosJsonCheckpointStore(IMafCheckpointDocumentStore store) => _store = store;

    public async ValueTask<CheckpointInfo> CreateCheckpointAsync(string sessionId, JsonElement value, CheckpointInfo? parent)
    {
        var checkpointId = Guid.NewGuid().ToString("N");
        var info = new CheckpointInfo(sessionId, checkpointId);
        await _store.UpsertAsync(
            new MafCheckpointDocument(
                sessionId,
                checkpointId,
                MafCheckpointDocumentStore.CycleFromSession(sessionId),
                parent?.CheckpointId,
                DateTimeOffset.UtcNow,
                value.GetRawText()),
            CancellationToken.None);
        return info;
    }

    public async ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key)
    {
        var document = await _store.GetAsync(sessionId, key.CheckpointId, CancellationToken.None)
            ?? throw new InvalidOperationException($"MAF checkpoint '{key.CheckpointId}' was not found for session '{sessionId}'.");
        using var json = JsonDocument.Parse(document.PayloadJson);
        return json.RootElement.Clone();
    }

    public async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(string sessionId, CheckpointInfo? withParent)
    {
        var documents = await _store.ListBySessionAsync(sessionId, CancellationToken.None);
        IEnumerable<MafCheckpointDocument> filtered = documents;
        if (withParent is not null)
            filtered = documents.Where(d => d.ParentCheckpointId == withParent.CheckpointId);

        return filtered.Select(d => new CheckpointInfo(sessionId, d.CheckpointId)).ToList();
    }
}

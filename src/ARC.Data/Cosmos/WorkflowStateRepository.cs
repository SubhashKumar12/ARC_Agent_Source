using Microsoft.Azure.Cosmos;
using ARC.Data.Exceptions;
using ARC.Data.Serialization;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;

namespace ARC.Data.Cosmos;

public sealed class WorkflowStateRepository : IWorkflowStateRepository
{
    private readonly ICosmosClientFactory _cosmos;

    public WorkflowStateRepository(ICosmosClientFactory cosmos) => _cosmos = cosmos;

    public async Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint, RecoveryState state, CancellationToken cancellationToken)
    {
        var document = CheckpointDocument.From(checkpoint, state);
        try
        {
            await _cosmos.Checkpoints.UpsertItemAsync(document, new PartitionKey(document.cycleId), cancellationToken: cancellationToken);
            await SaveStateAsync(state, cancellationToken);
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to save workflow checkpoint.", ex);
        }
    }

    public async Task<(WorkflowCheckpoint Checkpoint, RecoveryState State)?> LoadCheckpointAsync(
        CycleId cycleId, DealerUrn dealerUrn, string node, CancellationToken cancellationToken)
    {
        var id = CheckpointDocument.IdFor(cycleId.Value, dealerUrn.Value, node);
        try
        {
            var response = await _cosmos.Checkpoints.ReadItemAsync<CheckpointDocument>(
                id, new PartitionKey(cycleId.Value), cancellationToken: cancellationToken);
            return response.Resource.ToDomain();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to load workflow checkpoint.", ex);
        }
    }

    public async Task<RecoveryState?> LoadLatestStateAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
    {
        var id = CycleStateDocument.IdFor(cycleId.Value, dealerUrn.Value);
        try
        {
            var response = await _cosmos.CycleState.ReadItemAsync<CycleStateDocument>(
                id, new PartitionKey(cycleId.Value), cancellationToken: cancellationToken);
            return ArcJson.Deserialize<RecoveryState>(response.Resource.stateJson);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to load cycle state.", ex);
        }
    }

    public async Task SaveStateAsync(RecoveryState state, CancellationToken cancellationToken)
    {
        var document = CycleStateDocument.From(state);
        try
        {
            await _cosmos.CycleState.UpsertItemAsync(document, new PartitionKey(document.cycleId), cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to save cycle state.", ex);
        }
    }

    private sealed class CheckpointDocument
    {
        public string id { get; set; } = "";
        public string cycleId { get; set; } = "";
        public string dealerUrn { get; set; } = "";
        public string node { get; set; } = "";
        public string status { get; set; } = "";
        public DateTimeOffset capturedUtc { get; set; }
        public string stateJson { get; set; } = "";

        public static string IdFor(string cycleId, string dealerUrn, string node)
            => $"{cycleId}:{dealerUrn}:{node}";

        public static CheckpointDocument From(WorkflowCheckpoint checkpoint, RecoveryState state) => new()
        {
            id = IdFor(checkpoint.CycleId.Value, checkpoint.DealerUrn.Value, checkpoint.Node),
            cycleId = checkpoint.CycleId.Value,
            dealerUrn = checkpoint.DealerUrn.Value,
            node = checkpoint.Node,
            status = checkpoint.Status.ToString(),
            capturedUtc = checkpoint.CapturedUtc,
            stateJson = ArcJson.Serialize(state)
        };

        public (WorkflowCheckpoint Checkpoint, RecoveryState State) ToDomain()
        {
            var checkpoint = new WorkflowCheckpoint(
                new CycleId(cycleId),
                new DealerUrn(dealerUrn),
                node,
                Enum.Parse<WorkflowStatus>(status, ignoreCase: true),
                capturedUtc);
            return (checkpoint, ArcJson.Deserialize<RecoveryState>(stateJson));
        }
    }

    private sealed class CycleStateDocument
    {
        public string id { get; set; } = "";
        public string cycleId { get; set; } = "";
        public string dealerUrn { get; set; } = "";
        public string status { get; set; } = "";
        public string stateJson { get; set; } = "";

        public static string IdFor(string cycleId, string dealerUrn) => $"{cycleId}:{dealerUrn}";

        public static CycleStateDocument From(RecoveryState state) => new()
        {
            id = IdFor(state.CycleId.Value, state.DealerUrn.Value),
            cycleId = state.CycleId.Value,
            dealerUrn = state.DealerUrn.Value,
            status = state.Status.ToString(),
            stateJson = ArcJson.Serialize(state)
        };
    }
}

using ARC.Data.Cosmos;
using ARC.Data.Sql;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;

namespace ARC.Agents.Workflows.Persistence;

public sealed class WorkflowNodePersistence
{
    private readonly IWorkflowStateRepository _states;
    private readonly IRecoveryCaseRepository _index;

    public WorkflowNodePersistence(IWorkflowStateRepository states, IRecoveryCaseRepository index)
    {
        _states = states;
        _index = index;
    }

    public async Task SaveAsync(string node, RecoveryState state, CancellationToken cancellationToken)
    {
        var checkpoint = new WorkflowCheckpoint(
            state.CycleId,
            state.DealerUrn,
            node,
            state.Status,
            DateTimeOffset.UtcNow);
        await _states.SaveCheckpointAsync(checkpoint, state, cancellationToken);
        await _states.SaveStateAsync(state, cancellationToken);
        await _index.UpsertIndexAsync(
            new RecoveryCaseIndex(
                state.CycleId,
                state.DealerUrn,
                state.Status.ToString(),
                state.CorrelationId.Value,
                state.WaitingGate?.ToString(),
                DateTimeOffset.UtcNow,
                state.Risk?.Score,
                state.Risk?.Tier.ToString()),
            cancellationToken);
    }

    public Task<(WorkflowCheckpoint Checkpoint, RecoveryState State)?> LoadAsync(
        RecoveryState state,
        string node,
        CancellationToken cancellationToken)
        => _states.LoadCheckpointAsync(state.CycleId, state.DealerUrn, node, cancellationToken);

    public Task<RecoveryState?> LoadLatestAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
        => _states.LoadLatestStateAsync(cycleId, dealerUrn, cancellationToken);
}

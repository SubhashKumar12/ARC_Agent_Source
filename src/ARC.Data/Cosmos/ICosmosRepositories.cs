using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;

namespace ARC.Data.Cosmos;

public interface IWorkflowStateRepository
{
    Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint, RecoveryState state, CancellationToken cancellationToken);
    Task<(WorkflowCheckpoint Checkpoint, RecoveryState State)?> LoadCheckpointAsync(
        CycleId cycleId, DealerUrn dealerUrn, string node, CancellationToken cancellationToken);
    Task<RecoveryState?> LoadLatestStateAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken);
    Task SaveStateAsync(RecoveryState state, CancellationToken cancellationToken);
}

public interface IConversationStateRepository
{
    Task SaveAsync(CycleId cycleId, DealerUrn dealerUrn, string payloadJson, CancellationToken cancellationToken);
    Task<string?> GetAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken);
}

public interface IAuditRepository
{
    Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}

/// <summary>MAF runtime checkpoint documents. Partition: cycleId. Distinct from node RecoveryState checkpoints.</summary>
public interface IMafCheckpointDocumentStore
{
    Task UpsertAsync(MafCheckpointDocument document, CancellationToken cancellationToken);
    Task<MafCheckpointDocument?> GetAsync(string sessionId, string checkpointId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MafCheckpointDocument>> ListBySessionAsync(string sessionId, CancellationToken cancellationToken);
}

public sealed record MafCheckpointDocument(
    string SessionId,
    string CheckpointId,
    string CycleId,
    string? ParentCheckpointId,
    DateTimeOffset CommittedUtc,
    string PayloadJson);

public sealed record AuditEvent(
    string EventType,
    string CycleId,
    string? DealerUrn,
    string CorrelationId,
    DateTimeOffset OccurredUtc,
    string? Detail = null);

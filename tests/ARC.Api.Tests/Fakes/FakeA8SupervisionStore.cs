using ARC.Data.Cosmos;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;

namespace ARC.Api.Tests.Fakes;

/// <summary>
/// In-memory A8 reads for API tests. Does not open SQL or Cosmos.
/// Seeds one stored gate for DEALER-NORTH so chat-safe metadata can be asserted.
/// </summary>
public sealed class FakeA8SupervisionStore :
    IRecoveryCaseRepository,
    IGateDecisionRepository,
    IWorkflowStateRepository
{
    private readonly Dictionary<string, RecoveryCaseIndex> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<GateDecision>> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecoveryState> _state = new(StringComparer.OrdinalIgnoreCase);

    public FakeA8SupervisionStore()
    {
        var cycle = new CycleId("C1");
        var north = new DealerUrn("DEALER-NORTH");
        _index[Key(cycle, north)] = new RecoveryCaseIndex(
            cycle,
            north,
            nameof(WorkflowStatus.WaitingForHuman),
            "corr-c1-north",
            nameof(GateId.DepotManager),
            DateTimeOffset.Parse("2026-03-01T00:00:00Z"));
        _gates[Key(cycle, north)] =
        [
            GateDecision.Create(
                GateId.DepotManager,
                "depot.manager@paintco.local",
                ActorRole.DepotManager,
                GateDecisionStatus.Approved,
                "ok",
                new CorrelationId("corr-c1-north-gate"))
        ];
    }

    public Task UpsertIndexAsync(RecoveryCaseIndex index, CancellationToken cancellationToken)
    {
        _index[Key(index.CycleId, index.DealerUrn)] = index;
        return Task.CompletedTask;
    }

    public Task<RecoveryCaseIndex?> GetAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
        => Task.FromResult(_index.GetValueOrDefault(Key(cycleId, dealerUrn)));

    public Task<IReadOnlyList<RecoveryCaseIndex>> ListByCycleAsync(
        CycleId cycleId, string? region, string? depot, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<RecoveryCaseIndex>>(
            _index.Values.Where(i => i.CycleId.Value == cycleId.Value).ToList());

    public Task<RankedWorklist> ListRankedWorklistAsync(
        CycleId cycleId, string? region, string? depot, bool topDecile, CancellationToken cancellationToken)
        => Task.FromResult(new RankedWorklist(cycleId, topDecile, 0, []));

    public Task SaveAsync(CycleId cycleId, DealerUrn dealerUrn, GateDecision decision, CancellationToken cancellationToken)
    {
        var key = Key(cycleId, dealerUrn);
        if (!_gates.TryGetValue(key, out var list))
        {
            list = [];
            _gates[key] = list;
        }

        list.Add(decision);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GateDecision>> ListAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<GateDecision>>(_gates.GetValueOrDefault(Key(cycleId, dealerUrn)) ?? []);

    public Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint, RecoveryState state, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<(WorkflowCheckpoint Checkpoint, RecoveryState State)?> LoadCheckpointAsync(
        CycleId cycleId, DealerUrn dealerUrn, string node, CancellationToken cancellationToken)
        => Task.FromResult<(WorkflowCheckpoint Checkpoint, RecoveryState State)?>(null);

    public Task<RecoveryState?> LoadLatestStateAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
        => Task.FromResult(_state.GetValueOrDefault(Key(cycleId, dealerUrn)));

    public Task SaveStateAsync(RecoveryState state, CancellationToken cancellationToken)
    {
        _state[$"{state.CycleId.Value}|{state.DealerUrn.Value}"] = state;
        return Task.CompletedTask;
    }

    private static string Key(CycleId cycleId, DealerUrn dealerUrn) => $"{cycleId.Value}|{dealerUrn.Value}";
}

using ARC.Agents.Tests.Fakes;
using ARC.Agents.Workflows.Persistence;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;

namespace ARC.Agents.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task Node_checkpoint_is_idempotent_on_cycle_dealer_node()
    {
        var store = new InMemoryHarness();
        var persistence = new WorkflowNodePersistence(store, store);
        var state = new RecoveryState
        {
            CycleId = new CycleId("2026-03"),
            DealerUrn = new DealerUrn("dealer:persist"),
            AsOf = new DateOnly(2026, 3, 1),
            CorrelationId = new CorrelationId("corr-1"),
            Mode = RunMode.Shadow,
            Status = WorkflowStatus.WaitingForHuman
        };

        await persistence.SaveAsync("A3", state, CancellationToken.None);
        var loaded = await persistence.LoadAsync(state, "A3", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("2026-03|dealer:persist|A3", loaded.Value.Checkpoint.IdempotencyKey);
        Assert.Equal(WorkflowStatus.WaitingForHuman, loaded.Value.State.Status);

        var missing = await persistence.LoadAsync(state, "A5", CancellationToken.None);
        Assert.Null(missing);

        var latest = await persistence.LoadLatestAsync(state.CycleId, state.DealerUrn, CancellationToken.None);
        Assert.Equal("corr-1", latest?.CorrelationId.Value);
    }
}

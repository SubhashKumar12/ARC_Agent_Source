using Microsoft.Extensions.DependencyInjection;
using ARC.Agents.Workflows;
using ARC.Agents.Workflows.Models;
using ARC.Data.Cosmos;
using ARC.Data.Serialization;
using ARC.Data.Sql;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;
using ARC.Host.Functions.Runtime;
using ARC.Integration.Tests.Fixtures;

namespace ARC.Integration.Tests.Durability;

[Trait("Category", "Integration")]
[Collection("AC2-ColdResume")]
public abstract class ColdResumeTestBase : IAsyncLifetime
{
    protected SqlFixture Sql { get; } = new();
    protected CosmosFixture Cosmos { get; } = new();
    protected ArcHostFixture Hosts { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await InfrastructureGate.EnsureAvailableAsync(CancellationToken.None);
        await Sql.InitializeAsync(CancellationToken.None);
        await Cosmos.InitializeAsync(CancellationToken.None);
        Hosts = new ArcHostFixture(Sql, Cosmos);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    protected static WorkflowRunRequest OdosRequest(string cycleId, string dealerUrn) => new()
    {
        CycleId = cycleId,
        DealerUrn = dealerUrn,
        AsOf = new DateOnly(2026, 3, 1),
        CorrelationId = $"corr-{cycleId}",
        Mode = RunMode.Shadow,
        Kind = ArcWorkflowKind.Odos
    };

    protected static async Task<(RecoveryState State, PendingGateHalt Halt, DateTimeOffset A1CapturedUtc, DateTimeOffset A2CapturedUtc, DateTimeOffset A3CapturedUtc)>
        RunUntilG1Async(IServiceProvider services, string cycleId, string dealerUrn, CancellationToken cancellationToken)
    {
        var runner = services.GetRequiredService<DealerWorkflowRunner>();
        await runner.RunAsync(OdosRequest(cycleId, dealerUrn), cancellationToken);

        var states = services.GetRequiredService<IWorkflowStateRepository>();
        var pending = services.GetRequiredService<IConversationStateRepository>();
        var cycle = new CycleId(cycleId);
        var urn = new DealerUrn(dealerUrn);

        var state = await states.LoadLatestStateAsync(cycle, urn, cancellationToken)
            ?? throw new InvalidOperationException("Expected RecoveryState after Host A run.");
        Assert.Equal(WorkflowStatus.WaitingForHuman, state.Status);
        Assert.Equal(GateId.DepotManager, state.WaitingGate);
        Assert.Equal(RunMode.Shadow, state.Mode);

        var pendingJson = await pending.GetAsync(cycle, urn, cancellationToken)
            ?? throw new InvalidOperationException("Expected PendingGateHalt in Cosmos conversation state.");
        var halt = ArcJson.Deserialize<PendingGateHalt>(pendingJson);
        Assert.Equal(ArcWorkflowNodes.GateDepotManager, halt.PortId);
        Assert.False(string.IsNullOrWhiteSpace(halt.CheckpointId));
        Assert.Equal(DealerWorkflowRunner.SessionId(cycleId, dealerUrn, ArcWorkflowKind.Odos), halt.SessionId);

        var a1 = await states.LoadCheckpointAsync(cycle, urn, ArcWorkflowNodes.A1, cancellationToken)
            ?? throw new InvalidOperationException("A1 node checkpoint missing.");
        var a2 = await states.LoadCheckpointAsync(cycle, urn, ArcWorkflowNodes.A2, cancellationToken)
            ?? throw new InvalidOperationException("A2 node checkpoint missing.");
        var a3 = await states.LoadCheckpointAsync(cycle, urn, ArcWorkflowNodes.A3, cancellationToken)
            ?? throw new InvalidOperationException("A3 node checkpoint missing.");

        return (state, halt, a1.Checkpoint.CapturedUtc, a2.Checkpoint.CapturedUtc, a3.Checkpoint.CapturedUtc);
    }

    protected static GateResumeRequest Resume(
        string cycleId,
        string dealerUrn,
        GateDecisionStatus decision,
        ActorRole role,
        string upn,
        string reason) => new()
    {
        CycleId = cycleId,
        DealerUrn = dealerUrn,
        Kind = ArcWorkflowKind.Odos,
        ActorUpn = upn,
        ActorRole = role,
        Decision = decision,
        Reason = reason
    };
}

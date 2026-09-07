using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.DependencyInjection;
using ARC.Agents.Workflows;
using ARC.Data.Cosmos;
using ARC.Data.Sql;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;
using ARC.Host.Functions.Checkpointing;
using ARC.Host.Functions.Runtime;
using ARC.Integration.Tests.Fixtures;

namespace ARC.Integration.Tests.Durability;

[Trait("Category", "Integration")]
[Collection("AC2-ColdResume")]
public sealed class ColdResume_Odos_G1_ApprovedTests : ColdResumeTestBase
{
    [Fact]
    public async Task ColdResume_Odos_G1_Approved_AcrossTwoHostLifetimes()
    {
        var dealerUrn = $"dealer:ac2-approved-{Guid.NewGuid():N}";
        var cycleId = $"2026-03-ac2-{Guid.NewGuid():N}"[..28];
        var sessionId = DealerWorkflowRunner.SessionId(cycleId, dealerUrn, ARC.Agents.Workflows.Models.ArcWorkflowKind.Odos);

        await Sql.SeedOdosDealerAsync(dealerUrn, CancellationToken.None);

        var outboundA = new RecordingOutboundGate();
        DateTimeOffset a1Utc, a2Utc, a3Utc;

        // ---- Host Instance A ----
        using (var hostA = Hosts.CreateHost(outboundA))
        {
            var servicesA = hostA.Services;
            Assert.IsType<CosmosJsonCheckpointStore>(
                servicesA.GetRequiredService<ICheckpointStore<System.Text.Json.JsonElement>>());

            (_, _, a1Utc, a2Utc, a3Utc) = await RunUntilG1Async(servicesA, cycleId, dealerUrn, CancellationToken.None);

            var mafCount = await Cosmos.CountMafCheckpointsAsync(sessionId, CancellationToken.None);
            Assert.True(mafCount >= 1, $"Expected durable MAF checkpoint(s) in Cosmos for session {sessionId}, found {mafCount}.");

            // Prove Host A uses a distinct DI root that will be disposed.
            Assert.NotNull(servicesA.GetRequiredService<DealerWorkflowRunner>());
        }

        // Host A disposed — no shared in-memory MAF store remains.

        var outboundB = new RecordingOutboundGate();

        // ---- Host Instance B (new DI root) ----
        using (var hostB = Hosts.CreateHost(outboundB))
        {
            var servicesB = hostB.Services;
            Assert.IsType<CosmosJsonCheckpointStore>(
                servicesB.GetRequiredService<ICheckpointStore<System.Text.Json.JsonElement>>());

            // Pending halt still durable after Host A death.
            var pendingStore = servicesB.GetRequiredService<IConversationStateRepository>();
            var pendingJson = await pendingStore.GetAsync(new CycleId(cycleId), new DealerUrn(dealerUrn), CancellationToken.None);
            Assert.False(string.IsNullOrWhiteSpace(pendingJson));

            var runnerB = servicesB.GetRequiredService<DealerWorkflowRunner>();
            await runnerB.ResumeAsync(
                Resume(
                    cycleId,
                    dealerUrn,
                    GateDecisionStatus.Approved,
                    ActorRole.DepotManager,
                    "depot.manager@paintco.local",
                    "AC#2 cold resume Approved — Host B restored Cosmos checkpoint."),
                CancellationToken.None);

            var states = servicesB.GetRequiredService<IWorkflowStateRepository>();
            var state = await states.LoadLatestStateAsync(new CycleId(cycleId), new DealerUrn(dealerUrn), CancellationToken.None)
                ?? throw new InvalidOperationException("Missing state after Host B resume.");

            Assert.Equal(RunMode.Shadow, state.Mode);
            Assert.Contains(state.Approvals, a => a.Gate == GateId.DepotManager && a.Decision == GateDecisionStatus.Approved);
            Assert.True(
                state.WaitingGate == GateId.AdvocateSignature
                || state.Status == WorkflowStatus.Completed
                || state.Status == WorkflowStatus.WaitingForHuman,
                $"Expected progression beyond G1; status={state.Status} waiting={state.WaitingGate}");
            Assert.True(state.WaitingGate != GateId.DepotManager, "Cold resume must leave G1 Depot Manager.");

            // Idempotency: A1/A2/A3 node checkpoints not rewritten as fresh work.
            var a1 = await states.LoadCheckpointAsync(new CycleId(cycleId), new DealerUrn(dealerUrn), ArcWorkflowNodes.A1, CancellationToken.None);
            var a2 = await states.LoadCheckpointAsync(new CycleId(cycleId), new DealerUrn(dealerUrn), ArcWorkflowNodes.A2, CancellationToken.None);
            var a3 = await states.LoadCheckpointAsync(new CycleId(cycleId), new DealerUrn(dealerUrn), ArcWorkflowNodes.A3, CancellationToken.None);
            Assert.NotNull(a1);
            Assert.NotNull(a2);
            Assert.NotNull(a3);
            Assert.Equal(a1Utc, a1.Value.Checkpoint.CapturedUtc);
            Assert.Equal(a2Utc, a2.Value.Checkpoint.CapturedUtc);
            Assert.Equal(a3Utc, a3.Value.Checkpoint.CapturedUtc);

            var gates = servicesB.GetRequiredService<IGateDecisionRepository>();
            var decisions = await gates.ListAsync(new CycleId(cycleId), new DealerUrn(dealerUrn), CancellationToken.None);
            Assert.Equal(1, decisions.Count(d => d.Gate == GateId.DepotManager));
            Assert.All(decisions.Where(d => d.Gate == GateId.DepotManager), d => Assert.Equal(GateDecisionStatus.Approved, d.Decision));
        }

        Assert.DoesNotContain(outboundA.Events, e => e.Contains("Live", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(outboundB.Events, e => e.Contains("Live", StringComparison.OrdinalIgnoreCase));
        // Notice despatch only after advocate path; G1 halt/resume must not Live-despatch.
        Assert.DoesNotContain(outboundA.Events, e => e.StartsWith("notice-suppressed", StringComparison.Ordinal));
    }
}

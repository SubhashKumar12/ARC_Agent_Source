using Microsoft.Extensions.DependencyInjection;
using ARC.Data.Cosmos;
using ARC.Data.Sql;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;
using ARC.Host.Functions.Runtime;
using ARC.Integration.Tests.Fixtures;

namespace ARC.Integration.Tests.Durability;

[Trait("Category", "Integration")]
[Collection("AC2-ColdResume")]
public sealed class ColdResume_Odos_G1_ExpiredTests : ColdResumeTestBase
{
    [Fact]
    public async Task ColdResume_Odos_G1_Expired_DoesNotProgress()
    {
        var dealerUrn = $"dealer:ac2-expired-{Guid.NewGuid():N}";
        var cycleId = $"2026-03-ac2x-{Guid.NewGuid():N}"[..28];
        var sessionId = DealerWorkflowRunner.SessionId(cycleId, dealerUrn, ARC.Agents.Workflows.Models.ArcWorkflowKind.Odos);

        await Sql.SeedOdosDealerAsync(dealerUrn, CancellationToken.None);

        using (var hostA = Hosts.CreateHost(new RecordingOutboundGate()))
        {
            await RunUntilG1Async(hostA.Services, cycleId, dealerUrn, CancellationToken.None);
            var mafCount = await Cosmos.CountMafCheckpointsAsync(sessionId, CancellationToken.None);
            Assert.True(mafCount >= 1, "MAF checkpoint must be durable before Host A dispose.");
        }

        var outboundB = new RecordingOutboundGate();
        using (var hostB = Hosts.CreateHost(outboundB))
        {
            var runnerB = hostB.Services.GetRequiredService<DealerWorkflowRunner>();
            await runnerB.ResumeAsync(
                Resume(
                    cycleId,
                    dealerUrn,
                    GateDecisionStatus.Expired,
                    ActorRole.Finance,
                    "system.gate-timer",
                    "gate_expired"),
                CancellationToken.None);

            var states = hostB.Services.GetRequiredService<IWorkflowStateRepository>();
            var state = await states.LoadLatestStateAsync(new CycleId(cycleId), new DealerUrn(dealerUrn), CancellationToken.None)
                ?? throw new InvalidOperationException("Missing state after expired resume.");

            Assert.Equal(RunMode.Shadow, state.Mode);
            Assert.Contains(state.Approvals, a => a.Gate == GateId.DepotManager && a.Decision == GateDecisionStatus.Expired);
            var expired = state.Approvals.Single(a => a.Gate == GateId.DepotManager);
            Assert.False(expired.AllowsProgression);
            Assert.Equal("gate_expired", expired.Reason);

            Assert.True(
                state.Status is WorkflowStatus.Terminated or WorkflowStatus.Blocked,
                $"Expiry must not approve; status={state.Status}");
            Assert.Null(state.WaitingGate);

            var gates = hostB.Services.GetRequiredService<IGateDecisionRepository>();
            var decisions = await gates.ListAsync(new CycleId(cycleId), new DealerUrn(dealerUrn), CancellationToken.None);
            Assert.DoesNotContain(decisions, d => d.Gate == GateId.DepotManager && d.Decision == GateDecisionStatus.Approved);

            Assert.DoesNotContain(outboundB.Events, e => e.StartsWith("notice-suppressed", StringComparison.Ordinal));
            Assert.DoesNotContain(outboundB.Events, e => e.Contains("Live", StringComparison.OrdinalIgnoreCase));
        }
    }
}

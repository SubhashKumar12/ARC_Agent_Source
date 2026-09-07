using Microsoft.Extensions.Logging.Abstractions;
using ARC.Agents.Workflows.Outbound;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;
using ARC.Tools.Field;

namespace ARC.Agents.Tests;

public sealed class ShadowOutboundTests
{
    [Fact]
    public async Task Shadow_gate_completes_without_throwing_and_does_not_require_live_despatch()
    {
        var gate = new ShadowOutboundGate(NullLogger<ShadowOutboundGate>.Instance);
        var state = new RecoveryState
        {
            CycleId = new CycleId("2026-03"),
            DealerUrn = new DealerUrn("dealer:shadow"),
            AsOf = new DateOnly(2026, 3, 1),
            CorrelationId = new CorrelationId("corr-shadow"),
            Mode = RunMode.Shadow
        };
        var visit = new VisitTask("2026-03|dealer:shadow|visit", "dealer:shadow", "depot", "West", "tsi", RecoveryTier.Notice, state.AsOf);

        await gate.OnNoticeReadyAsync(state, CancellationToken.None);
        await gate.OnVisitPlannedAsync(visit, state, CancellationToken.None);
    }
}

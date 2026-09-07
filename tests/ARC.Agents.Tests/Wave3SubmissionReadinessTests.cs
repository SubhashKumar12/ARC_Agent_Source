using ARC.Agents.Tests.Support;
using ARC.Agents.Workflows;
using ARC.Agents.Workflows.Outbound;
using Microsoft.Extensions.DependencyInjection;
using ARC.Domain.BusinessChat;
using ARC.Domain.Enums;
using ARC.Domain.Readiness;
using ARC.Domain.Rules;

namespace ARC.Agents.Tests;

/// <summary>Wave 3 submission closure — local evidence that assignment guardrails remain intact.</summary>
public sealed class Wave3SubmissionReadinessTests
{
    [Fact]
    public void Shadow_outbound_gate_is_default_for_agents_host()
    {
        using var host = AgentTestHost.Create().Services;
        Assert.IsType<ShadowOutboundGate>(host.GetRequiredService<IOutboundGate>());
    }

    [Fact]
    public void R4_prevents_recommending_agent_from_gate_approval()
    {
        Assert.False(R4SegregationOfDuties.CanApprove(ActorRole.Agent));
        Assert.True(R4SegregationOfDuties.CanApprove(ActorRole.DepotManager));
        Assert.True(R4SegregationOfDuties.CanApprove(ActorRole.Advocate));
    }

    [Theory]
    [InlineData(ArcWorkflowNodes.GateDepotManager)]
    [InlineData(ArcWorkflowNodes.GateAdvocateSignature)]
    [InlineData(ArcWorkflowNodes.GateLegalProgression)]
    [InlineData(ArcWorkflowNodes.GateLegalCaseFileReview)]
    public void Four_HITL_RequestPort_gate_ids_are_defined(string gateId)
    {
        Assert.False(string.IsNullOrWhiteSpace(gateId));
    }

    [Fact]
    public void Business_chat_unsupported_exposure_remains_fail_closed_in_catalog()
    {
        var metrics = BusinessMetricCatalog.MatchMetrics("compute net recoverable exposure for this dealer");
        Assert.Contains(BusinessMetric.NetRecoverableExposure, metrics);
        Assert.Equal(BusinessCapability.DealerFinancialAdjustments, BusinessMetricCatalog.GetCapability(BusinessMetric.NetRecoverableExposure));
    }

    [Fact]
    public void External_decision_register_documents_unresolved_TBC_items()
    {
        Assert.True(ExternalDecisionRegistry.All.Count >= 28);
        Assert.Contains(ExternalDecisionRegistry.All, d => d.DecisionId == "DEC-L04");
        Assert.Contains(ExternalDecisionRegistry.All, d => d.DecisionId == "DEC-A03");
    }
}

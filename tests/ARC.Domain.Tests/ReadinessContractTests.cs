using ARC.Domain.Readiness;

namespace ARC.Domain.Tests;

public sealed class ReadinessContractTests
{
    [Fact]
    public void External_decision_registry_has_28_entries()
    {
        Assert.Equal(34, ExternalDecisionRegistry.All.Count);
        Assert.All(ExternalDecisionRegistry.All, d => Assert.False(string.IsNullOrWhiteSpace(d.DecisionId)));
    }

    [Fact]
    public void Production_capabilities_dealer_facts_ready_for_get_async()
    {
        var dealer = ProductionCapabilityCatalog.Build()
            .First(c => c.Capability == ProductionCapabilityKind.DealerFacts);

        Assert.Equal(ProductionCapabilityGate.ReadyLocal, dealer.Gate);
        Assert.Contains("usp_ARC_GetDealer", dealer.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Mcp_tool_catalog_marks_interim_ranking()
    {
        var tool = McpToolReadinessCatalog.Build()
            .First(t => t.ToolName == "prioritiseRecovery");

        Assert.Equal(ReadinessLevel.Interim, tool.Level);
        Assert.Contains(McpToolReadinessFlag.ExternalDecisionBlocked, tool.Flags);
    }
}

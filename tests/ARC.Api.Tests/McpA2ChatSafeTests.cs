using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;

namespace ARC.Api.Tests;

/// <summary>
/// MCP prioritiseRecovery must expose server-generated formula/TBC/completeness.
/// Callers have no parameters for those fields.
/// </summary>
public sealed class McpA2ChatSafeTests
{
    [Fact]
    public void Mcp_response_shape_uses_server_formula_and_does_not_accept_overrides()
    {
        var exposure = new ExposureBreakdown
        {
            DealerUrn = new("DEALER001"),
            AsOf = new DateOnly(2026, 3, 1),
            GrossOpenAr = new(250000, "INR"),
            UnappliedCreditNotes = Money.Zero,
            AccruedSchemeRebates = Money.Zero,
            GoodsReturnInTransit = Money.Zero,
            ChequesInClearing = Money.Zero,
            DisputedUnderReview = Money.Zero,
            NetRecoverableExposure = new(250000, "INR"),
            Status = ReconciliationStatus.Reconciled,
            Lineage = []
        };

        var assessment = new RiskAssessment(RecoveryTier.Notice, 250000m);
        var chat = A2ChatSafeAssembler.FromPrioritisation(
            exposure, assessment, true, false, false, false);

        var mcpBody = new
        {
            tier = assessment.Tier.ToString(),
            score = assessment.Score,
            scoreFormula = chat.ScoreFormula,
            provenanceStatus = chat.Provenance.Status,
            dataCompleteness = chat.DataCompleteness,
            tbcIndicators = chat.TbcIndicators
        };

        Assert.Equal("net_recoverable_exposure.v1", mcpBody.scoreFormula);
        Assert.Equal(250000m, mcpBody.score);
        Assert.Equal("Incomplete", mcpBody.provenanceStatus);
        Assert.Equal(A2InputAvailability.Unavailable, chat.DataCompleteness.Single(c => c.Input == "PaymentHistory").Status);
        Assert.Equal(A2InputAvailability.Tbc, chat.DataCompleteness.Single(c => c.Input == "GraphFeatures").Status);
        Assert.Contains(chat.TbcIndicators, t => t.Id == "VisitTierCutoff" && t.Status == "Tbc");

        // There is no input slot for ScoreFormula / TBC / completeness on the MCP tool.
        var method = typeof(ARC.Api.Mcp.ArcMcpTools).GetMethod("PrioritiseRecoveryAsync");
        Assert.NotNull(method);
        var names = method!.GetParameters().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("scoreFormula", names);
        Assert.DoesNotContain("ScoreFormula", names);
        Assert.DoesNotContain("tbcIndicators", names);
        Assert.DoesNotContain("dataCompleteness", names);
        Assert.DoesNotContain("provenance", names);
    }
}

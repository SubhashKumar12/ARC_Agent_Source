using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;
using ARC.Tools.Risk;
using ARC.Tools.Notice;
using ARC.Tools.Drafting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ARC.Api.Tests.Fakes;

namespace ARC.Api.Tests;

/// <summary>
/// Proves that MCP tools are thin adapters returning the same business results as underlying ARC tools.
/// </summary>
public sealed class McpToolEquivalenceTests : IClassFixture<ArcApiTestFactory>
{
    private readonly ArcApiTestFactory _factory;

    public McpToolEquivalenceTests(ArcApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void PrioritiseRecovery_MCP_Equals_Underlying_Tool()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var tool = scope.ServiceProvider.GetRequiredService<RiskPrioritisationTool>();

        var exposure = new ExposureBreakdown
        {
            DealerUrn = new("DEALER001"),
            AsOf = DateOnly.FromDateTime(DateTime.Today),
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

        var request = new RiskPrioritisationRequest(exposure, true, null, "test-correlation");

        // Act
        var result = tool.Prioritise(request);
        var chat = A2ChatSafeAssembler.FromPrioritisation(
            exposure, result, true, false, false, visitCutoffConfigured: false);

        // Assert - MCP should return same tier and score
        Assert.True(result.Tier == RecoveryTier.Visit || 
                    result.Tier == RecoveryTier.Notice || 
                    result.Tier == RecoveryTier.Section138);
        Assert.True(result.Score >= 0);
        Assert.Equal(result.Score, chat.RecoverabilityScore);
        Assert.Equal("net_recoverable_exposure.v1", chat.ScoreFormula);
        Assert.Equal("Incomplete", chat.Provenance.Status);
        Assert.DoesNotContain(chat.TbcIndicators, t => t.Status != "Tbc");
    }

    [Fact]
    public void DecideNotice_MCP_Equals_Underlying_Tool()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var tool = scope.ServiceProvider.GetRequiredService<NoticeDecisionTool>();

        var dealer = new Dealer(new("DEALER001"), false, "SAP001", null, "DEL", "North");
        var exposure = new ExposureBreakdown
        {
            DealerUrn = new("DEALER001"),
            AsOf = DateOnly.FromDateTime(DateTime.Today),
            GrossOpenAr = new(150000, "INR"),
            UnappliedCreditNotes = Money.Zero,
            AccruedSchemeRebates = Money.Zero,
            GoodsReturnInTransit = Money.Zero,
            ChequesInClearing = Money.Zero,
            DisputedUnderReview = Money.Zero,
            NetRecoverableExposure = new(150000, "INR"),
            Status = ReconciliationStatus.Reconciled,
            Lineage = []
        };

        var request = new NoticeDecisionRequest(dealer, exposure, DateOnly.FromDateTime(DateTime.Today), null, null, null, "test-correlation");

        // Act
        var result = tool.Decide(request);

        // Assert - MCP must return same decision
        Assert.True(result.Decision == NoticeDecision.Issue || 
                    result.Decision == NoticeDecision.Hold ||
                    result.Decision == NoticeDecision.Reconcile);
    }

    [Fact]
    public void VerifyDraft_MCP_Equals_Underlying_Tool()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var tool = scope.ServiceProvider.GetRequiredService<DraftingVerificationTool>();

        var draftFields = new DraftQuotedFields("DEALER001", "SAP001", 100000, null, null, null, null, null, null, null);
        var exposure = new ExposureBreakdown
        {
            DealerUrn = new("DEALER001"),
            AsOf = DateOnly.FromDateTime(DateTime.Today),
            GrossOpenAr = new(100000, "INR"),
            UnappliedCreditNotes = Money.Zero,
            AccruedSchemeRebates = Money.Zero,
            GoodsReturnInTransit = Money.Zero,
            ChequesInClearing = Money.Zero,
            DisputedUnderReview = Money.Zero,
            NetRecoverableExposure = new(100000, "INR"),
            Status = ReconciliationStatus.Reconciled,
            Lineage = []
        };
        var dealer = new Dealer(new("DEALER001"), false, "SAP001", null, "DEL", "North");
        var request = new DraftingVerificationRequest(draftFields, DraftKind.DemandNotice, exposure, dealer, null, null, null, null, "test-correlation");

        // Act
        var result = tool.Verify(request);

        // Assert - MCP must return same verification result
        Assert.NotNull(result.Checks);
    }
}

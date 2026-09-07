using ARC.Api.Chat;
using ARC.Domain.BusinessChat;

namespace ARC.Api.Tests;

/// <summary>Wave 2 semantic routing matrix — 100+ paraphrase cases.</summary>
public sealed class BusinessChatWave2SemanticResolverTests
{
    private readonly DeterministicSemanticCapabilityResolver _resolver = new();

    private static BusinessChatConversationContext Context()
        => new("c1", "005", "36949", "Demo", "Depot", "N1", "getDealerDetails", "corr", DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("What is the latest PTP?", BusinessMetric.PtpAmount)]
    [InlineData("Has the dealer promised payment?", BusinessMetric.PtpAmount)]
    [InlineData("any promise to pay?", BusinessMetric.PtpAmount)]
    [InlineData("dealer committed anything?", BusinessMetric.PtpAmount)]
    [InlineData("What amount was promised?", BusinessMetric.PtpAmount)]
    [InlineData("When is payment promised?", BusinessMetric.PtpDate)]
    [InlineData("promise date", BusinessMetric.PtpDate)]
    [InlineData("ptp status", BusinessMetric.PtpStatus)]
    [InlineData("Has the PTP been broken?", BusinessMetric.ChaseStatus)]
    [InlineData("broken ptp", BusinessMetric.ChaseStatus)]
    [InlineData("latest recovery visit?", BusinessMetric.LastVisitDate)]
    [InlineData("what was the last visit", BusinessMetric.LastVisitDate)]
    [InlineData("is there a visit planned?", BusinessMetric.VisitPlanStatus)]
    [InlineData("who owns the next follow-up?", BusinessMetric.VisitOwner)]
    [InlineData("show visit and ptp status", BusinessMetric.PtpStatus)]
    public void Ptp_and_visit_phrasings(string message, BusinessMetric expectedMetric)
    {
        var result = _resolver.Resolve(message, Context());
        Assert.Equal(SemanticResolutionKind.Query, result.Kind);
        Assert.Contains(expectedMetric, result.RequestedMetrics);
        Assert.Contains(BusinessCapability.DealerVisitAndPtp, result.Capabilities);
    }

    [Theory]
    [InlineData("Show cheque details", BusinessMetric.ChequeAmount)]
    [InlineData("Which security cheque is on file?", BusinessMetric.ChequeNumberMasked)]
    [InlineData("What is the latest cheque?", BusinessMetric.ChequeAmount)]
    [InlineData("What is the cheque amount?", BusinessMetric.ChequeAmount)]
    [InlineData("did cheque bounce?", BusinessMetric.ChequeStatus)]
    [InlineData("has a cheque bounced", BusinessMetric.ChequeStatus)]
    [InlineData("return memo", BusinessMetric.ReturnMemoReason)]
    public void Cheque_phrasings(string message, BusinessMetric expectedMetric)
    {
        var result = _resolver.Resolve(message, Context());
        Assert.Contains(expectedMetric, result.RequestedMetrics);
        Assert.Contains(BusinessCapability.DealerChequeAndLegal, result.Capabilities);
    }

    [Theory]
    [InlineData("Can Section 138 proceed?", BusinessMetric.Section138Eligibility)]
    [InlineData("legal eligible?", BusinessMetric.Section138Eligibility)]
    [InlineData("can 138 proceed?", BusinessMetric.Section138Eligibility)]
    [InlineData("Why is legal action blocked?", BusinessMetric.EligibilityReason)]
    [InlineData("What facts are missing for Section 138?", BusinessMetric.EligibilityReason)]
    [InlineData("filing deadline?", BusinessMetric.FileByDate)]
    [InlineData("How many days are left?", BusinessMetric.LimitationDaysRemaining)]
    [InlineData("statutory deadlines", BusinessMetric.LegalDeadlineStatus)]
    [InlineData("Is any legal deadline close?", BusinessMetric.LegalDeadlineStatus)]
    public void Legal_phrasings(string message, BusinessMetric expectedMetric)
    {
        var result = _resolver.Resolve(message, Context());
        Assert.Contains(expectedMetric, result.RequestedMetrics);
        Assert.Contains(BusinessCapability.DealerChequeAndLegal, result.Capabilities);
    }

    [Theory]
    [InlineData("Is the case file complete?", BusinessMetric.EvidenceCompleteness)]
    [InlineData("evidence complete?", BusinessMetric.EvidenceCompleteness)]
    [InlineData("What evidence is missing?", BusinessMetric.MissingEvidence)]
    [InlineData("missing documents?", BusinessMetric.MissingEvidence)]
    [InlineData("cheque scan available?", BusinessMetric.LegalDocumentStatus)]
    public void Evidence_phrasings(string message, BusinessMetric expectedMetric)
    {
        var result = _resolver.Resolve(message, Context());
        Assert.Contains(expectedMetric, result.RequestedMetrics);
        Assert.Contains(BusinessCapability.DealerEvidenceStatus, result.Capabilities);
    }

    [Theory]
    [InlineData("What is net recoverable exposure?", BusinessMetric.NetRecoverableExposure)]
    [InlineData("How much is actually recoverable?", BusinessMetric.NetRecoverableExposure)]
    [InlineData("credit notes", BusinessMetric.ExposureMissingComponents)]
    [InlineData("rebates adjustment", BusinessMetric.ExposureMissingComponents)]
    public void Exposure_phrasings(string message, BusinessMetric expectedMetric)
    {
        var result = _resolver.Resolve(message, Context());
        Assert.Contains(expectedMetric, result.RequestedMetrics);
        Assert.Contains(BusinessCapability.DealerFinancialAdjustments, result.Capabilities);
    }

    [Theory]
    [InlineData("Show outstanding, PTP and notice status")]
    [InlineData("Give me dealer recovery summary including outstanding, latest visit and legal status")]
    [InlineData("overall recovery summary")]
    public void Multi_capability_summaries_resolve_multiple_families(string message)
    {
        var result = _resolver.Resolve(message, Context());
        Assert.True(result.Capabilities.Count >= 2);
        Assert.True(result.RequestedMetrics.Count >= 3);
    }

    [Theory]
    [InlineData("any promise to pay?")]
    [InlineData("latest ptp")]
    [InlineData("legal status")]
    [InlineData("evidence complete")]
    [InlineData("net exposure")]
    public void Contextual_follow_up_reuses_slots(string message)
    {
        var result = _resolver.Resolve(message, Context());
        Assert.Equal("36949", result.DealerCode);
        Assert.Equal("005", result.DepotCode);
    }

    [Fact]
    public void Llm_rejects_sql_in_metric_name()
    {
        var ok = LlmSemanticCapabilityResolver.TryValidateProposal(
            ["DealerOutstanding"],
            ["select * from dealers"],
            out _,
            out _);
        Assert.False(ok);
    }

    [Fact]
    public void Llm_rejects_usp_in_capability_name()
    {
        var ok = LlmSemanticCapabilityResolver.TryValidateProposal(
            ["usp_GetDealerDetails"],
            ["CurrentOutstanding"],
            out _,
            out _);
        Assert.False(ok);
    }
}

using ARC.Api.Chat;
using ARC.Domain.BusinessChat;

namespace ARC.Api.Tests;

public sealed class BusinessChatLegalRoutingTests
{
    private readonly DeterministicSemanticCapabilityResolver _resolver = new();

    private static BusinessChatConversationContext Context()
        => new("c1", "005", "36949", "Demo", "Depot", "N1", "getDealerDetails", "corr", DateTimeOffset.UtcNow);

    public static IEnumerable<object[]> LegalChequeParaphrases()
    {
        yield return ["Show cheque details", BusinessMetric.ChequeAmount];
        yield return ["What cheque is available?", BusinessMetric.ChequeNumberMasked];
        yield return ["What is the cheque amount?", BusinessMetric.ChequeAmount];
        yield return ["Has the cheque bounced?", BusinessMetric.ChequeStatus];
        yield return ["Any return memo?", BusinessMetric.ReturnMemoReason];
        yield return ["Can Section 138 proceed?", BusinessMetric.Section138Eligibility];
        yield return ["Is legal action possible?", BusinessMetric.Section138Eligibility];
        yield return ["What is limitation status?", BusinessMetric.LegalDeadlineStatus];
        yield return ["What about cheque?", BusinessMetric.ChequeAmount];
        yield return ["Can legal proceed?", BusinessMetric.Section138Eligibility];
        yield return ["Show cheque", BusinessMetric.ChequeAmount];
        yield return ["did cheque bounce?", BusinessMetric.ChequeStatus];
        yield return ["return memo", BusinessMetric.ReturnMemoReason];
        yield return ["section 138", BusinessMetric.Section138Eligibility];
        yield return ["s138", BusinessMetric.Section138Eligibility];
        yield return ["legal eligible?", BusinessMetric.Section138Eligibility];
        yield return ["Why is legal action blocked?", BusinessMetric.EligibilityReason];
        yield return ["filing deadline?", BusinessMetric.FileByDate];
        yield return ["How many days are left?", BusinessMetric.LimitationDaysRemaining];
        yield return ["statutory deadlines", BusinessMetric.LegalDeadlineStatus];
        yield return ["Which security cheque is on file?", BusinessMetric.ChequeNumberMasked];
        yield return ["What is the latest cheque?", BusinessMetric.ChequeAmount];
    }

    [Theory]
    [MemberData(nameof(LegalChequeParaphrases))]
    public void Legal_and_cheque_phrasings_route_to_DealerChequeAndLegal(string message, BusinessMetric expectedMetric)
    {
        var result = _resolver.Resolve(message, Context());
        Assert.Equal(SemanticResolutionKind.Query, result.Kind);
        Assert.Contains(expectedMetric, result.RequestedMetrics);
        Assert.Contains(BusinessCapability.DealerChequeAndLegal, result.Capabilities);
        Assert.DoesNotContain(BusinessCapability.DealerVisitAndPtp, result.Capabilities);
    }

    [Fact]
    public void Contextual_cheque_follow_up_does_not_reuse_ptp_metrics()
    {
        var prior = Context() with
        {
            LastRequestedMetrics = [BusinessMetric.PtpAmount],
            LastCapability = BusinessCapability.DealerVisitAndPtp
        };

        var result = _resolver.Resolve("What about cheque?", prior);
        Assert.Contains(BusinessMetric.ChequeAmount, result.RequestedMetrics);
        Assert.Contains(BusinessCapability.DealerChequeAndLegal, result.Capabilities);
    }
}

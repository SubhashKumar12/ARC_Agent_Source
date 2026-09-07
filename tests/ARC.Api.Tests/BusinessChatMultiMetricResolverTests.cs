using ARC.Api.Chat;
using ARC.Domain.BusinessChat;

namespace ARC.Api.Tests;

public sealed class BusinessChatMultiMetricResolverTests
{
    private readonly DeterministicSemanticCapabilityResolver _resolver = new();

    private static BusinessChatConversationContext Context()
        => new("c1", "020", "115014", "ROSHAN", "Depot", "C1", "getDealerDetails", "corr", DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("Give me outstanding and notice status.", BusinessMetric.CurrentOutstanding, BusinessMetric.NoticeGenerated)]
    [InlineData("Give me outstanding and legal status.", BusinessMetric.CurrentOutstanding, BusinessMetric.LegalStatus)]
    [InlineData("Show ageing and notice.", BusinessMetric.AgeingBucket0, BusinessMetric.NoticeGenerated)]
    public void Multi_metric_phrases_resolve_only_requested_metrics(
        string message,
        BusinessMetric first,
        BusinessMetric second)
    {
        var result = _resolver.Resolve(message, Context());

        Assert.Equal(SemanticResolutionKind.Query, result.Kind);
        Assert.Contains(first, result.RequestedMetrics);
        Assert.Contains(second, result.RequestedMetrics);
        Assert.DoesNotContain(BusinessMetric.Section138Eligibility, result.RequestedMetrics);
        Assert.True(result.RequestedMetrics.Count <= 10);
    }

    [Theory]
    [InlineData("Has TSI visited this dealer?", BusinessMetric.VisitStatus)]
    [InlineData("How many TSI visits are recorded?", BusinessMetric.TsiVisitCount)]
    [InlineData("What is the dealer feedback?", BusinessMetric.DealerFeedback)]
    public void Tsi_visit_phrases_resolve_odos_aggregate_metrics(string message, BusinessMetric expected)
    {
        var result = _resolver.Resolve(message, Context());

        Assert.Contains(expected, result.RequestedMetrics);
        Assert.Contains(BusinessCapability.DealerOdosVisitAggregate, result.Capabilities);
    }
}

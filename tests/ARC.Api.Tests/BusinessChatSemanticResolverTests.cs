using ARC.Api.Chat;
using ARC.Domain.BusinessChat;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ARC.Api.Tests;

public sealed class BusinessChatSemanticResolverTests
{
    private readonly DeterministicSemanticCapabilityResolver _deterministic = new();

    private static BusinessChatConversationContext Context(
        string? periodKey = null,
        BusinessCapability? lastCapability = null,
        IReadOnlyList<BusinessMetric>? lastMetrics = null)
        => new(
            "c1",
            "005",
            "36949",
            "Demo",
            "Depot",
            "N1",
            "getDealerDetails",
            "corr",
            DateTimeOffset.UtcNow,
            periodKey,
            lastCapability,
            lastMetrics);

    [Theory]
    [InlineData("Show dealer 36949 in depot 005", BusinessCapability.DealerIdentity)]
    [InlineData("What is dealer name for dealer 36949 in depot 005?", BusinessCapability.DealerIdentity)]
    [InlineData("depot name for dealer 36949 depot 005", BusinessCapability.DealerIdentity)]
    [InlineData("region for dealer 36949 in depot 005", BusinessCapability.DealerIdentity)]
    [InlineData("territory for dealer 36949 in depot 005", BusinessCapability.DealerIdentity)]
    [InlineData("bill to for dealer 36949 in depot 005", BusinessCapability.DealerIdentity)]
    [InlineData("customer type for dealer 36949 in depot 005", BusinessCapability.DealerIdentity)]
    [InlineData("mother account for dealer 36949 in depot 005", BusinessCapability.DealerIdentity)]
    public void Identity_phrasings_resolve_to_capability(string message, BusinessCapability expected)
    {
        var result = _deterministic.Resolve(message, prior: null);
        Assert.Equal(SemanticResolutionKind.Query, result.Kind);
        Assert.Contains(expected, result.Capabilities);
    }

    [Theory]
    [InlineData("What is his outstanding?")]
    [InlineData("Show current OS")]
    [InlineData("How much is due?")]
    [InlineData("How much due?")]
    [InlineData("amount due")]
    [InlineData("current outstanding")]
    [InlineData("odos details")]
    [InlineData("their outstanding")]
    public void Outstanding_phrasings_resolve_to_current_metric(string message)
    {
        var result = _deterministic.Resolve(message, Context());
        Assert.Equal(SemanticResolutionKind.Query, result.Kind);
        Assert.Contains(BusinessMetric.CurrentOutstanding, result.RequestedMetrics);
        Assert.Contains(BusinessCapability.DealerOutstanding, result.Capabilities);
    }

    [Theory]
    [InlineData("Show ageing breakup")]
    [InlineData("Give aging breakup")]
    [InlineData("breakup")]
    [InlineData("break up")]
    [InlineData("how old is the outstanding")]
    [InlineData("ageing")]
    public void Ageing_phrasings_resolve_to_ageing_metric(string message)
    {
        var result = _deterministic.Resolve(message, Context());
        Assert.Contains(BusinessMetric.AgeingBucket0, result.RequestedMetrics);
    }

    [Theory]
    [InlineData("What is above 90?")]
    [InlineData("over 90")]
    [InlineData(">90")]
    [InlineData("more than 90")]
    [InlineData("overdue more than 90")]
    public void Over90_phrasings_resolve_to_over90_metric(string message)
    {
        var result = _deterministic.Resolve(message, Context());
        Assert.Contains(BusinessMetric.Over90Outstanding, result.RequestedMetrics);
    }

    [Theory]
    [InlineData("Has notice been generated?")]
    [InlineData("What is the notice status?")]
    [InlineData("demand notice")]
    [InlineData("has notice")]
    [InlineData("any notice")]
    [InlineData("been generated")]
    public void Notice_phrasings_resolve_to_notice_metric(string message)
    {
        var result = _deterministic.Resolve(message, Context());
        Assert.True(
            result.RequestedMetrics.Contains(BusinessMetric.NoticeGenerated)
            || result.RequestedMetrics.Contains(BusinessMetric.NoticeDepotStatus)
            || result.RequestedMetrics.Contains(BusinessMetric.NoticeHoStatus));
        Assert.Contains(BusinessCapability.DealerRecoveryStatus, result.Capabilities);
    }

    [Theory]
    [InlineData("What is the recovery status?")]
    [InlineData("odos status")]
    [InlineData("What is legal status?")]
    [InlineData("case status")]
    public void Recovery_phrasings_resolve_to_recovery_capability(string message)
    {
        var result = _deterministic.Resolve(message, Context());
        Assert.Contains(BusinessCapability.DealerRecoveryStatus, result.Capabilities);
    }

    [Theory]
    [InlineData("Show outstanding and notice status")]
    [InlineData("Give me a summary of this dealer")]
    [InlineData("Show outstanding, ageing and notice status")]
    [InlineData("full details")]
    [InlineData("complete details")]
    public void Multi_metric_phrasings_resolve_multiple_capabilities(string message)
    {
        var result = _deterministic.Resolve(message, Context());
        Assert.Equal(SemanticResolutionKind.Query, result.Kind);
        Assert.True(result.RequestedMetrics.Count >= 2);
        Assert.Contains(BusinessCapability.DealerOutstanding, result.Capabilities);
        Assert.True(result.Capabilities.Contains(BusinessCapability.DealerRecoveryStatus)
                    || result.RequestedMetrics.Contains(BusinessMetric.DealerName));
    }

    [Theory]
    [InlineData("What is his outstanding?")]
    [InlineData("Show current OS")]
    [InlineData("legal status")]
    [InlineData("recovery status")]
    [InlineData("notice status")]
    public void Contextual_follow_up_reuses_dealer_and_depot_slots(string message)
    {
        var result = _deterministic.Resolve(message, Context());
        Assert.Equal("36949", result.DealerCode);
        Assert.Equal("005", result.DepotCode);
        Assert.False(result.ClarificationNeeded);
    }

    [Theory]
    [InlineData("Show dealer 36949")]
    [InlineData("dealer 36949")]
    [InlineData("Show dealer 36949 details")]
    public void Missing_depot_requests_clarification(string message)
    {
        var result = _deterministic.Resolve(message, prior: null);
        Assert.Equal(SemanticResolutionKind.Query, result.Kind);
        Assert.True(result.ClarificationNeeded || result.DealerCodeOnly);
        Assert.Equal("36949", result.DealerCode);
    }

    [Theory]
    [InlineData("What is net exposure?", BusinessCapability.DealerFinancialAdjustments)]
    [InlineData("Is dealer eligible for s138?", BusinessCapability.DealerChequeAndLegal)]
    [InlineData("section 138 eligibility", BusinessCapability.DealerChequeAndLegal)]
    [InlineData("compute exposure for dealer", BusinessCapability.DealerFinancialAdjustments)]
    public void Wave2_phrasings_route_to_capabilities(string message, BusinessCapability expected)
    {
        var result = _deterministic.Resolve(message, Context());
        Assert.Equal(SemanticResolutionKind.Query, result.Kind);
        Assert.Contains(expected, result.Capabilities);
    }

    [Theory]
    [InlineData("Which depot is this dealer under?")]
    [InlineData("What depot?")]
    public void Depot_follow_up_uses_prior_context(string message)
    {
        var result = _deterministic.Resolve(message, Context());
        Assert.Equal(SemanticResolutionKind.DepotFollowUp, result.Kind);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("what is the weather")]
    public void Unrecognized_questions_return_unrecognized(string message)
    {
        var result = _deterministic.Resolve(message, Context());
        Assert.Equal(SemanticResolutionKind.Unrecognized, result.Kind);
    }

    [Fact]
    public void Same_semantic_question_produces_same_capability_and_metrics()
    {
        var a = _deterministic.Resolve("How much is due?", Context());
        var b = _deterministic.Resolve("What is his outstanding?", Context());
        Assert.Equal(a.Capabilities.OrderBy(c => c), b.Capabilities.OrderBy(c => c));
        Assert.Contains(BusinessMetric.CurrentOutstanding, a.RequestedMetrics);
        Assert.Contains(BusinessMetric.CurrentOutstanding, b.RequestedMetrics);
    }

    [Fact]
    public void Composite_resolver_falls_back_when_llm_disabled()
    {
        var composite = new CompositeSemanticCapabilityResolver(
            _deterministic,
            new LlmSemanticCapabilityResolver(
                Options.Create(new BusinessChatSemanticOptions { UseLlmResolver = false }),
                NullLogger<LlmSemanticCapabilityResolver>.Instance));

        var result = composite.Resolve("Show current OS", Context());
        Assert.Equal("deterministic", result.ResolverSource);
        Assert.Contains(BusinessMetric.CurrentOutstanding, result.RequestedMetrics);
    }

    [Fact]
    public void Llm_proposal_rejects_unknown_capability()
    {
        var ok = LlmSemanticCapabilityResolver.TryValidateProposal(
            ["NotARealCapability"],
            ["CurrentOutstanding"],
            out _,
            out _);
        Assert.False(ok);
    }

    [Fact]
    public void Llm_proposal_rejects_unknown_metric()
    {
        var ok = LlmSemanticCapabilityResolver.TryValidateProposal(
            ["DealerOutstanding"],
            ["NotARealMetric"],
            out _,
            out _);
        Assert.False(ok);
    }

    [Fact]
    public void Llm_proposal_rejects_sql_injection()
    {
        var ok = LlmSemanticCapabilityResolver.TryValidateProposal(
            ["select * from dealers"],
            ["CurrentOutstanding"],
            out _,
            out _);
        Assert.False(ok);
    }

    [Fact]
    public void Llm_proposal_rejects_tool_injection()
    {
        var ok = LlmSemanticCapabilityResolver.TryValidateProposal(
            ["DealerOutstanding"],
            ["getDealerDetails"],
            out _,
            out _);
        Assert.False(ok);
    }

    [Fact]
    public void Llm_proposal_accepts_valid_catalog_values()
    {
        var ok = LlmSemanticCapabilityResolver.TryValidateProposal(
            ["DealerOutstanding", "DealerRecoveryStatus"],
            ["CurrentOutstanding", "NoticeGenerated"],
            out var caps,
            out var metrics);
        Assert.True(ok);
        Assert.Equal(2, caps.Count);
        Assert.Equal(2, metrics.Count);
    }

    [Fact]
    public void Metric_catalog_version_and_count_are_stable()
    {
        Assert.Equal("2.0.0", BusinessMetricCatalog.Version);
        Assert.Equal(52, BusinessMetricCatalog.AllMetrics.Count);
    }
}

public sealed class BusinessChatResponseComposerTests
{
    [Fact]
    public void Composer_renders_requested_metrics_only()
    {
        var bundle = new BusinessChatFactBundle(
            "36949",
            "005",
            new DealerIdentityFacts("36949", "Demo", "005", "Depot", "N1", null, null, null, null, null, null, null, null, null),
            new DealerFinancialFacts("2026-07", 100m, 10m, 20m, 30m, 25m, 15m, 5m, 50_000m),
            new DealerRecoveryFacts("Y", "Y", "Y", null, null, null, null, "01", "Demand Notice 1", null, null));

        var text = BusinessChatResponseComposer.Compose(bundle, [BusinessMetric.CurrentOutstanding]);
        Assert.Contains("Current Outstanding", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Ageing breakup", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Demand notice", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Composer_multi_metric_includes_financial_and_recovery_sections()
    {
        var bundle = new BusinessChatFactBundle(
            "36949",
            "005",
            null,
            new DealerFinancialFacts("2026-07", 100m, 10m, 20m, 30m, 25m, 15m, 5m, null),
            new DealerRecoveryFacts("Y", null, null, null, null, null, null, "01", "Demand Notice 1", null, null));

        var text = BusinessChatResponseComposer.Compose(
            bundle,
            [BusinessMetric.CurrentOutstanding, BusinessMetric.NoticeGenerated, BusinessMetric.RecoveryStatus]);

        Assert.Contains("Current Outstanding", text, StringComparison.Ordinal);
        Assert.Contains("Demand notice generated", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Recovery status", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Composer_outstanding_and_notice_emits_both_without_legal_status()
    {
        var bundle = new BusinessChatFactBundle(
            "115014",
            "020",
            null,
            new DealerFinancialFacts("2026-07", 7_225_124.13m, -125_000m, 0m, 81_403.68m, 7_268_720.45m, 0m, 7_350_124.13m, 5_000m),
            new DealerRecoveryFacts("Y", null, null, null, null, null, null, "00", "New", "00", "New"));

        var text = BusinessChatResponseComposer.Compose(
            bundle,
            [BusinessMetric.CurrentOutstanding, BusinessMetric.NoticeGenerated]);

        Assert.Contains("Current Outstanding", text, StringComparison.Ordinal);
        Assert.Contains("Demand notice generated: Y", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Legal status", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Composer_outstanding_and_legal_status_emits_both()
    {
        var bundle = new BusinessChatFactBundle(
            "115014",
            "020",
            null,
            new DealerFinancialFacts("2026-07", 7_225_124.13m, -125_000m, 0m, 81_403.68m, 7_268_720.45m, 0m, 7_350_124.13m, 5_000m),
            new DealerRecoveryFacts("Y", null, null, null, null, null, null, "00", "New", "00", "New"));

        var text = BusinessChatResponseComposer.Compose(
            bundle,
            [BusinessMetric.CurrentOutstanding, BusinessMetric.LegalStatus]);

        Assert.Contains("Current Outstanding", text, StringComparison.Ordinal);
        Assert.Contains("Legal status: 00 (New)", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Composer_ageing_and_notice_emits_both()
    {
        var bundle = new BusinessChatFactBundle(
            "115014",
            "020",
            null,
            new DealerFinancialFacts("2026-07", 7_225_124.13m, -125_000m, 0m, 81_403.68m, 7_268_720.45m, 0m, 7_350_124.13m, 5_000m),
            new DealerRecoveryFacts("Y", null, null, null, null, null, null, "00", "New", "00", "New"));

        var text = BusinessChatResponseComposer.Compose(
            bundle,
            [BusinessMetric.AgeingBucket0, BusinessMetric.NoticeGenerated]);

        Assert.Contains("Ageing breakup", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Demand notice generated: Y", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Composer_reports_available_and_unavailable_requested_metrics()
    {
        var bundle = new BusinessChatFactBundle(
            "115014",
            "020",
            null,
            null,
            new DealerRecoveryFacts("Y", null, null, null, null, null, null, "00", "New", "00", "New"),
            UnavailableMetrics: [new MetricUnavailability(BusinessMetric.CurrentOutstanding, "Outstanding data not found.")]);

        var text = BusinessChatResponseComposer.Compose(
            bundle,
            [BusinessMetric.CurrentOutstanding, BusinessMetric.NoticeGenerated]);

        Assert.Contains("CurrentOutstanding: not available", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Demand notice generated: Y", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Composer_renders_tsi_visit_aggregate_from_field_facts()
    {
        var bundle = new BusinessChatFactBundle(
            "115014",
            "020",
            null,
            null,
            null,
            new DealerFieldFacts(null, null, null, null, null, null, "No", null, null, 0, null));

        var text = BusinessChatResponseComposer.Compose(
            bundle,
            [BusinessMetric.VisitStatus, BusinessMetric.TsiVisitCount]);

        Assert.Contains("TSI visit status: No", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TSI visit count: 0", text, StringComparison.OrdinalIgnoreCase);
    }
}

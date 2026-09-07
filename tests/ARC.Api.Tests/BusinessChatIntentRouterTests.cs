using ARC.Api.Chat;
using ARC.Domain.Metrics;

namespace ARC.Api.Tests;

public sealed class BusinessChatIntentRouterTests
{
    [Fact]
    public void Routes_dealer_and_depot_to_get_dealer_details()
    {
        var intent = BusinessChatIntentRouter.Route("Show dealer 36949 in depot 005", prior: null);
        Assert.Equal(BusinessChatIntentKind.GetDealerDetails, intent.Kind);
        Assert.Equal("36949", intent.Slots.DealerCode);
        Assert.Equal("005", intent.Slots.DepotCode);
    }

    [Fact]
    public void Routes_dealer_only_to_clarification_path()
    {
        var intent = BusinessChatIntentRouter.Route("Show dealer 36949", prior: null);
        Assert.Equal(BusinessChatIntentKind.GetDealerDetails, intent.Kind);
        Assert.True(intent.Slots.DealerCodeOnly);
    }

    [Fact]
    public void Routes_exposure_question_to_financial_adjustments_capability()
    {
        var intent = BusinessChatIntentRouter.Route("What is net exposure?", prior: null);
        Assert.Equal(BusinessChatIntentKind.GetOutstandingDetails, intent.Kind);
    }

    [Theory]
    [InlineData("What is the outstanding?")]
    [InlineData("Show current OS")]
    [InlineData("How much is due?")]
    [InlineData("Give me outstanding details")]
    public void Routes_outstanding_phrasings_to_outstanding_capability(string message)
    {
        var prior = new BusinessChatConversationContext(
            "c1", "005", "36949", "Demo", "Depot", "N1", "getDealerDetails", "corr", DateTimeOffset.UtcNow);
        var intent = BusinessChatIntentRouter.Route(message, prior);
        Assert.Equal(BusinessChatIntentKind.GetOutstandingDetails, intent.Kind);
        Assert.Equal("36949", intent.Slots.DealerCode);
        Assert.Equal("005", intent.Slots.DepotCode);
    }

    [Theory]
    [InlineData("Show ageing", OutstandingAnswerFocus.Ageing)]
    [InlineData("Give ageing breakup", OutstandingAnswerFocus.Ageing)]
    [InlineData("How old is the outstanding?", OutstandingAnswerFocus.Ageing)]
    public void Routes_ageing_phrasings(string message, OutstandingAnswerFocus focus)
    {
        var prior = Context();
        var intent = BusinessChatIntentRouter.Route(message, prior);
        Assert.Equal(BusinessChatIntentKind.GetOutstandingDetails, intent.Kind);
        Assert.Equal(focus, intent.OutstandingFocus);
    }

    [Theory]
    [InlineData("How much is above 90 days?")]
    [InlineData("What is >90 outstanding?")]
    [InlineData("Show overdue more than 90 days")]
    public void Routes_over90_phrasings(string message)
    {
        var intent = BusinessChatIntentRouter.Route(message, Context());
        Assert.Equal(BusinessChatIntentKind.GetOutstandingDetails, intent.Kind);
        Assert.Equal(OutstandingAnswerFocus.Over90, intent.OutstandingFocus);
    }

    [Theory]
    [InlineData("What is the notice status?")]
    [InlineData("Has notice been generated?")]
    [InlineData("Any notice for this dealer?")]
    public void Routes_notice_phrasings(string message)
    {
        var intent = BusinessChatIntentRouter.Route(message, Context());
        Assert.Equal(BusinessChatIntentKind.GetOutstandingDetails, intent.Kind);
        Assert.Equal(OutstandingAnswerFocus.NoticeStatus, intent.OutstandingFocus);
    }

    [Fact]
    public void Routes_outstanding_with_explicit_dealer_and_depot()
    {
        var intent = BusinessChatIntentRouter.Route(
            "What is outstanding for dealer 36949 in depot 005?",
            prior: null);
        Assert.Equal(BusinessChatIntentKind.GetOutstandingDetails, intent.Kind);
        Assert.Equal("36949", intent.Slots.DealerCode);
        Assert.Equal("005", intent.Slots.DepotCode);
    }

    [Fact]
    public void Outstanding_question_does_not_route_to_unsupported_exposure()
    {
        var intent = BusinessChatIntentRouter.Route("What is outstanding?", Context());
        Assert.NotEqual(BusinessChatIntentKind.UnsupportedCapability, intent.Kind);
    }

    private static BusinessChatConversationContext Context()
        => new("c1", "005", "36949", "Demo", "Depot", "N1", "getDealerDetails", "corr", DateTimeOffset.UtcNow);
}

public sealed class BusinessChatNarrationBuilderTests
{
    [Fact]
    public void Narration_does_not_include_mobile()
    {
        var facts = new DealerDetailChatSafeContract(
            "urn",
            "36949",
            "Demo",
            "005",
            "Depot",
            "N1",
            "North",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            DealerDetailChatSafeAssembler.PublishedDataSource);

        var text = BusinessChatNarrationBuilder.BuildDealerDetailAnswer(facts);
        Assert.DoesNotContain("mobile", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Outstanding_narration_includes_over90_and_ageing()
    {
        var facts = new OutstandingDetailChatSafeContract(
            "36949",
            "005",
            "Demo",
            "Depot",
            "2026-07",
            100m,
            10m,
            20m,
            30m,
            25m,
            15m,
            90m,
            50_000m,
            "01",
            "Demand Notice 1",
            null,
            null,
            "Y",
            "Y",
            "Y",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 2),
            "Depot ok",
            "HO ok",
            null,
            null,
            null,
            ["ODOS.usp_GetDealerDetails"]);

        var text = BusinessChatNarrationBuilder.BuildOutstandingAnswer(facts, OutstandingAnswerFocus.Full);
        Assert.Contains("Current Outstanding", text, StringComparison.Ordinal);
        Assert.Contains(">90 Days Outstanding", text, StringComparison.Ordinal);
        Assert.Contains("Ageing breakup", text, StringComparison.Ordinal);
        Assert.DoesNotContain("usp_", text, StringComparison.OrdinalIgnoreCase);
    }
}

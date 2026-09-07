using System.Text.RegularExpressions;
using ARC.Domain.BusinessChat;

namespace ARC.Api.Chat;

/// <summary>
/// Legacy compatibility adapter over <see cref="DeterministicSemanticCapabilityResolver"/>.
/// New code should use <see cref="ISemanticCapabilityResolver"/> directly.
/// </summary>
public enum BusinessChatIntentKind
{
    Unknown = 0,
    GetDealerDetails = 1,
    FollowUpDepotFromContext = 2,
    UnsupportedCapability = 3,
    GetOutstandingDetails = 4,
}

public enum OutstandingAnswerFocus
{
    Full = 0,
    CurrentOnly = 1,
    Ageing = 2,
    Over90 = 3,
    OdosStatus = 4,
    NoticeStatus = 5,
}

public sealed record BusinessChatDealerSlots(
    string? DealerCode,
    string? DepotCode,
    bool DealerCodeOnly);

public sealed record BusinessChatIntent(
    BusinessChatIntentKind Kind,
    BusinessChatDealerSlots Slots,
    string? UnsupportedCapabilityHint,
    OutstandingAnswerFocus OutstandingFocus = OutstandingAnswerFocus.Full);

public static partial class BusinessChatDealerSlotParser
{
    public static BusinessChatDealerSlots Parse(string message)
    {
        var text = message.Trim();
        if (TryMatch(DealerInDepotPattern(), text, out var dealer, out var depot)
            || TryMatch(DepotDealerPattern(), text, out depot, out dealer))
        {
            return new BusinessChatDealerSlots(dealer, depot, DealerCodeOnly: false);
        }

        var dealerOnly = DealerOnlyPattern().Match(text);
        if (dealerOnly.Success)
            return new BusinessChatDealerSlots(dealerOnly.Groups[1].Value, null, DealerCodeOnly: true);

        return new BusinessChatDealerSlots(null, null, DealerCodeOnly: false);
    }

    public static bool IsDepotFollowUpQuestion(string message)
    {
        var text = message.Trim();
        return DepotFollowUpPattern().IsMatch(text);
    }

    private static bool TryMatch(Regex pattern, string text, out string first, out string second)
    {
        first = second = "";
        var match = pattern.Match(text);
        if (!match.Success)
            return false;
        first = match.Groups[1].Value;
        second = match.Groups[2].Value;
        return true;
    }

    [GeneratedRegex(@"\bdealer\s+(\d+)\s+(?:in\s+)?depot\s+(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DealerInDepotPattern();

    [GeneratedRegex(@"\bdepot\s+(\d+)\s+dealer\s+(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DepotDealerPattern();

    [GeneratedRegex(@"\bdealer\s+(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DealerOnlyPattern();

    [GeneratedRegex(@"\b(which|what)\s+depot\b|\bdepot\s+(is\s+)?this\s+dealer\s+under\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DepotFollowUpPattern();
}

[Obsolete("Use BusinessMetricCatalog via ISemanticCapabilityResolver. Retained for transitional tests.")]
public static class BusinessChatOutstandingCues
{
    public static bool IsOutstandingFamily(string message)
        => BusinessMetricCatalog.MatchMetrics(message)
            .Any(m => BusinessMetricCatalog.GetCapability(m) is BusinessCapability.DealerOutstanding
                      or BusinessCapability.DealerRecoveryStatus);

    public static OutstandingAnswerFocus DetectFocus(string message)
    {
        var metrics = BusinessMetricCatalog.MatchMetrics(message).ToHashSet();
        if (metrics.Contains(BusinessMetric.NoticeGenerated)
            || metrics.Contains(BusinessMetric.NoticeDepotStatus)
            || metrics.Contains(BusinessMetric.NoticeHoStatus)
            || metrics.Contains(BusinessMetric.NoticeDate))
            return OutstandingAnswerFocus.NoticeStatus;
        if (metrics.Contains(BusinessMetric.Over90Outstanding))
            return OutstandingAnswerFocus.Over90;
        if (metrics.Contains(BusinessMetric.AgeingBucket0)
            || metrics.Contains(BusinessMetric.AgeingBucket1)
            || metrics.Contains(BusinessMetric.AgeingBucket2)
            || metrics.Contains(BusinessMetric.AgeingBucket3)
            || metrics.Contains(BusinessMetric.AgeingBucket4))
            return OutstandingAnswerFocus.Ageing;
        if (metrics.Contains(BusinessMetric.CurrentOutstanding) && metrics.Count == 1)
            return OutstandingAnswerFocus.CurrentOnly;
        if (metrics.Contains(BusinessMetric.RecoveryStatus) || metrics.Contains(BusinessMetric.LegalStatus))
            return OutstandingAnswerFocus.OdosStatus;
        return OutstandingAnswerFocus.Full;
    }
}

public static class BusinessChatIntentRouter
{
    private static readonly DeterministicSemanticCapabilityResolver Resolver = new();

    public static BusinessChatIntent Route(string message, BusinessChatConversationContext? prior)
    {
        var resolution = Resolver.Resolve(message, prior);
        var slots = BusinessChatDealerSlotParser.Parse(message);

        return resolution.Kind switch
        {
            SemanticResolutionKind.UnsupportedCapability => new BusinessChatIntent(
                BusinessChatIntentKind.UnsupportedCapability,
                slots,
                resolution.UnsupportedCapabilityHint),
            SemanticResolutionKind.DepotFollowUp => new BusinessChatIntent(
                BusinessChatIntentKind.FollowUpDepotFromContext,
                slots,
                null),
            SemanticResolutionKind.Query => MapQuery(resolution),
            _ => new BusinessChatIntent(BusinessChatIntentKind.Unknown, slots, null),
        };
    }

    private static BusinessChatIntent MapQuery(SemanticResolutionResult resolution)
    {
        var slots = new BusinessChatDealerSlots(
            resolution.DealerCode,
            resolution.DepotCode,
            resolution.DealerCodeOnly);

        if (resolution.ClarificationNeeded || resolution.DealerCodeOnly)
            return new BusinessChatIntent(BusinessChatIntentKind.GetDealerDetails, slots, null);

        var hasOutstanding = resolution.Capabilities.Contains(BusinessCapability.DealerOutstanding)
            || resolution.Capabilities.Contains(BusinessCapability.DealerRecoveryStatus)
            || resolution.Capabilities.Contains(BusinessCapability.DealerVisitAndPtp)
            || resolution.Capabilities.Contains(BusinessCapability.DealerChequeAndLegal)
            || resolution.Capabilities.Contains(BusinessCapability.DealerEvidenceStatus)
            || resolution.Capabilities.Contains(BusinessCapability.DealerFinancialAdjustments);

        if (hasOutstanding)
        {
            return new BusinessChatIntent(
                BusinessChatIntentKind.GetOutstandingDetails,
                slots,
                null,
                MapFocus(resolution.RequestedMetrics));
        }

        if (resolution.Capabilities.Contains(BusinessCapability.DealerIdentity))
            return new BusinessChatIntent(BusinessChatIntentKind.GetDealerDetails, slots, null);

        return new BusinessChatIntent(BusinessChatIntentKind.Unknown, slots, null);
    }

    private static OutstandingAnswerFocus MapFocus(IReadOnlyList<BusinessMetric> metrics)
    {
        var set = metrics.ToHashSet();
        if (set.Contains(BusinessMetric.NoticeGenerated)
            || set.Contains(BusinessMetric.NoticeDepotStatus)
            || set.Contains(BusinessMetric.NoticeHoStatus)
            || set.Contains(BusinessMetric.NoticeDate))
            return OutstandingAnswerFocus.NoticeStatus;
        if (set.Contains(BusinessMetric.Over90Outstanding))
            return OutstandingAnswerFocus.Over90;
        if (set.Contains(BusinessMetric.AgeingBucket0)
            || set.Contains(BusinessMetric.AgeingBucket1)
            || set.Contains(BusinessMetric.AgeingBucket2)
            || set.Contains(BusinessMetric.AgeingBucket3)
            || set.Contains(BusinessMetric.AgeingBucket4))
            return OutstandingAnswerFocus.Ageing;
        if (set.Contains(BusinessMetric.RecoveryStatus) || set.Contains(BusinessMetric.LegalStatus))
            return OutstandingAnswerFocus.OdosStatus;
        if (set.Contains(BusinessMetric.CurrentOutstanding) && set.Count == 1)
            return OutstandingAnswerFocus.CurrentOnly;
        return OutstandingAnswerFocus.Full;
    }
}

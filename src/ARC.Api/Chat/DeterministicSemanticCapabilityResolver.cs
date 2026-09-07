using ARC.Domain.BusinessChat;

namespace ARC.Api.Chat;

public interface ISemanticCapabilityResolver
{
    SemanticResolutionResult Resolve(string message, BusinessChatConversationContext? prior);
}

/// <summary>
/// Primary deterministic resolver. Catalog aliases drive metric detection — not per-question if/else.
/// </summary>
public sealed class DeterministicSemanticCapabilityResolver : ISemanticCapabilityResolver
{
    public SemanticResolutionResult Resolve(string message, BusinessChatConversationContext? prior)
    {
        var text = message.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return SemanticResolutionResult.Unrecognized();

        if (BusinessMetricCatalog.IsUnsupportedEligibilityRequest(text))
            return SemanticResolutionResult.Unsupported("checkSection138Eligibility");

        if (BusinessMetricCatalog.IsUnsupportedExposureRequest(text))
            return SemanticResolutionResult.Unsupported("computeNetExposure");

        var slots = BusinessChatDealerSlotParser.Parse(text);

        if (BusinessChatDealerSlotParser.IsDepotFollowUpQuestion(text) && prior?.DepotCode is not null)
            return SemanticResolutionResult.DepotFollowUp();

        var metrics = BusinessMetricCatalog.MatchMetrics(text).ToList();

        if (HasExplicitDealerDepot(slots))
        {
            var capabilitySet = metrics
                .Select(BusinessMetricCatalog.GetCapability)
                .ToHashSet();
            if (capabilitySet.Count == 1 && capabilitySet.Contains(BusinessCapability.DealerIdentity))
                metrics = BusinessMetricCatalog.MetricsForCapability(BusinessCapability.DealerIdentity).ToList();
        }
        else if (metrics.Count == 0 && !string.IsNullOrWhiteSpace(slots.DealerCode))
        {
            metrics = BusinessMetricCatalog.MetricsForCapability(BusinessCapability.DealerIdentity).ToList();
        }

        if (metrics.Count == 0 && prior?.LastRequestedMetrics is { Count: > 0 } && IsContextualFollowUp(text))
            metrics = prior.LastRequestedMetrics.ToList();

        if (metrics.Count == 0)
            return SemanticResolutionResult.Unrecognized();

        var resolvedSlots = ResolveSlots(slots, prior);
        var capabilities = metrics
            .Select(BusinessMetricCatalog.GetCapability)
            .Distinct()
            .OrderBy(c => (int)c)
            .ToList();

        var clarification = resolvedSlots.DealerCodeOnly
            || (string.IsNullOrWhiteSpace(resolvedSlots.DepotCode) && !string.IsNullOrWhiteSpace(resolvedSlots.DealerCode));

        return new SemanticResolutionResult(
            SemanticResolutionKind.Query,
            metrics,
            capabilities,
            resolvedSlots.DealerCode,
            resolvedSlots.DepotCode,
            resolvedSlots.DealerCodeOnly,
            clarification,
            UnsupportedCapabilityHint: null,
            Confidence: 0.95,
            ResolverSource: "deterministic");
    }

    private static bool HasExplicitDealerDepot(BusinessChatDealerSlots slots)
        => !string.IsNullOrWhiteSpace(slots.DealerCode) && !string.IsNullOrWhiteSpace(slots.DepotCode);

    private static bool IsContextualFollowUp(string text)
    {
        if (BusinessMetricCatalog.MatchMetrics(text).Count > 0)
            return false;

        return ContainsAny(text,
            "his ",
            "her ",
            "their ",
            "this dealer",
            "same dealer",
            "about him",
            "about her",
            "about them");
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static BusinessChatDealerSlots ResolveSlots(
        BusinessChatDealerSlots slots,
        BusinessChatConversationContext? prior)
    {
        if (!string.IsNullOrWhiteSpace(slots.DealerCode) && !string.IsNullOrWhiteSpace(slots.DepotCode))
            return slots;

        if (prior?.DepotCode is not null && prior.DealerCode is not null)
        {
            return new BusinessChatDealerSlots(
                prior.DealerCode,
                prior.DepotCode,
                DealerCodeOnly: false);
        }

        return slots;
    }
}

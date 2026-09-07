using ARC.Domain.Readiness;
using ARC.Domain.Readiness.Chat;

namespace ARC.Api.Chat;

public static class BusinessChatToolRegistry
{
    public const string GetDealerDetails = "getDealerDetails";
    public const string GetOutstandingDetails = "getOutstandingDetails";
    public const string GetFieldRecoveryDetails = "getFieldRecoveryDetails";
    public const string GetLegalRecoveryDetails = "getLegalRecoveryDetails";
    public const string GetEvidenceStatus = "getEvidenceStatus";
    public const string GetNetExposureDetails = "getNetExposureDetails";

    private static readonly IReadOnlyDictionary<string, McpToolReadinessEntry> Catalog =
        McpToolReadinessCatalog.Build().ToDictionary(t => t.ToolName, StringComparer.OrdinalIgnoreCase);

    public static bool IsChatReady(string toolName)
    {
        if (!Catalog.TryGetValue(toolName, out var entry))
            return false;
        return entry.Flags.Contains(McpToolReadinessFlag.ChatSafe)
               && entry.Level == ReadinessLevel.Ready;
    }

    public static bool IsChatSupported(string toolName)
    {
        if (!Catalog.TryGetValue(toolName, out var entry))
            return false;
        if (!entry.Flags.Contains(McpToolReadinessFlag.ChatSafe))
            return false;
        return entry.Level is ReadinessLevel.Ready or ReadinessLevel.Interim or ReadinessLevel.Tbc;
    }

    public static ChatCapabilityStatus ToCapabilityStatus(string toolName)
    {
        if (!Catalog.TryGetValue(toolName, out var entry))
        {
            return new ChatCapabilityStatus(
                toolName,
                ChatCapabilityAvailability.UnavailableInsufficientEvidence,
                "Tool is not registered.",
                null);
        }

        if (!entry.Flags.Contains(McpToolReadinessFlag.ChatSafe))
        {
            return new ChatCapabilityStatus(
                toolName,
                ChatCapabilityAvailability.UnavailableInsufficientEvidence,
                "Tool is not chat-safe.",
                entry.BlockingDecisionId);
        }

        if (entry.Level == ReadinessLevel.Ready)
            return new ChatCapabilityStatus(toolName, ChatCapabilityAvailability.Available, entry.KnownCaveat, entry.BlockingDecisionId);

        if (entry.Level is ReadinessLevel.Interim or ReadinessLevel.Tbc)
        {
            return new ChatCapabilityStatus(
                toolName,
                ChatCapabilityAvailability.UnavailableExternalDecision,
                entry.KnownCaveat,
                entry.BlockingDecisionId);
        }

        return new ChatCapabilityStatus(
            toolName,
            entry.Level == ReadinessLevel.BlockedExternal
                ? ChatCapabilityAvailability.UnavailableProductionData
                : ChatCapabilityAvailability.UnavailableExternalDecision,
            entry.KnownCaveat,
            entry.BlockingDecisionId);
    }
}

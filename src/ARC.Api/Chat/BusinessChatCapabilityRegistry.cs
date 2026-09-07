using ARC.Domain.BusinessChat;
using ARC.Domain.Readiness;

namespace ARC.Api.Chat;

public sealed record BusinessCapabilityRegistryEntry(
    BusinessCapability Capability,
    string ToolName,
    ReadinessLevel Level,
    IReadOnlyList<BusinessMetric> AllowedMetrics,
    string DataSource);

/// <summary>
/// Single registry bridging business capabilities to underlying tools and readiness.
/// </summary>
public static class BusinessChatCapabilityRegistry
{
    public static IReadOnlyList<BusinessCapabilityRegistryEntry> All { get; } = Build();

    public static BusinessCapabilityRegistryEntry? Get(BusinessCapability capability)
        => All.FirstOrDefault(e => e.Capability == capability);

    public static string? ToolForCapability(BusinessCapability capability)
        => Get(capability)?.ToolName;

    public static bool IsReady(BusinessCapability capability)
    {
        var entry = Get(capability);
        return entry is not null
               && entry.Level == ReadinessLevel.Ready
               && BusinessChatToolRegistry.IsChatReady(entry.ToolName);
    }

    public static bool IsChatSupported(BusinessCapability capability)
    {
        var entry = Get(capability);
        return entry is not null && BusinessChatToolRegistry.IsChatSupported(entry.ToolName);
    }

    public static bool MetricsAllowed(BusinessCapability capability, IEnumerable<BusinessMetric> metrics)
    {
        var entry = Get(capability);
        if (entry is null)
            return false;
        var allowed = entry.AllowedMetrics.ToHashSet();
        return metrics.All(allowed.Contains);
    }

    private static IReadOnlyList<BusinessCapabilityRegistryEntry> Build() =>
    [
        new(
            BusinessCapability.DealerIdentity,
            BusinessChatToolRegistry.GetDealerDetails,
            ReadinessLevel.Ready,
            BusinessMetricCatalog.MetricsForCapability(BusinessCapability.DealerIdentity),
            "ODOS.usp_ARC_GetDealer"),
        new(
            BusinessCapability.DealerOutstanding,
            BusinessChatToolRegistry.GetOutstandingDetails,
            ReadinessLevel.Ready,
            BusinessMetricCatalog.MetricsForCapability(BusinessCapability.DealerOutstanding),
            "ODOS.usp_GetDealerDetails|usp_GetOpeningDataForArc"),
        new(
            BusinessCapability.DealerRecoveryStatus,
            BusinessChatToolRegistry.GetOutstandingDetails,
            ReadinessLevel.Ready,
            BusinessMetricCatalog.MetricsForCapability(BusinessCapability.DealerRecoveryStatus),
            "ODOS.usp_GetDealerDetails"),
        new(
            BusinessCapability.DealerOdosVisitAggregate,
            BusinessChatToolRegistry.GetOutstandingDetails,
            ReadinessLevel.Ready,
            BusinessMetricCatalog.MetricsForCapability(BusinessCapability.DealerOdosVisitAggregate),
            "ODOS.usp_GetDealerDetails"),
        new(
            BusinessCapability.DealerVisitAndPtp,
            BusinessChatToolRegistry.GetFieldRecoveryDetails,
            ReadinessLevel.Interim,
            BusinessMetricCatalog.MetricsForCapability(BusinessCapability.DealerVisitAndPtp),
            "dbo.usp_ListPtpCommittedByDealer|dbo.usp_ListPtpChasesByTsiStatus"),
        new(
            BusinessCapability.DealerChequeAndLegal,
            BusinessChatToolRegistry.GetLegalRecoveryDetails,
            ReadinessLevel.Tbc,
            BusinessMetricCatalog.MetricsForCapability(BusinessCapability.DealerChequeAndLegal),
            "ODOS.usp_GetSubmittedChequesForDealerODOS|CheckSection138Eligibility|GetLimitationClock"),
        new(
            BusinessCapability.DealerEvidenceStatus,
            BusinessChatToolRegistry.GetEvidenceStatus,
            ReadinessLevel.Ready,
            BusinessMetricCatalog.MetricsForCapability(BusinessCapability.DealerEvidenceStatus),
            "ODOS.usp_GetDealerDetails|ODOS.usp_GetLegalDocumentsODOS|dbo.usp_UpsertLegalCase overlay"),
        new(
            BusinessCapability.DealerFinancialAdjustments,
            BusinessChatToolRegistry.GetNetExposureDetails,
            ReadinessLevel.Interim,
            BusinessMetricCatalog.MetricsForCapability(BusinessCapability.DealerFinancialAdjustments),
            "ComputeNetExposure|DEC-O04|DEC-F03"),
    ];
}

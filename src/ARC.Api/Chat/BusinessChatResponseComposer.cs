using System.Globalization;
using ARC.Domain.BusinessChat;
using ARC.Domain.Odos;

namespace ARC.Api.Chat;

/// <summary>
/// Reusable deterministic response composer. Renders only requested metrics from fact envelopes.
/// </summary>
public static class BusinessChatResponseComposer
{
    private static readonly CultureInfo InrCulture = CultureInfo.GetCultureInfo("en-IN");

    public static string Compose(
        BusinessChatFactBundle bundle,
        IReadOnlyList<BusinessMetric> requestedMetrics)
    {
        var lines = new List<string>();
        var metricSet = requestedMetrics.ToHashSet();

        AppendIdentity(lines, bundle.Identity, metricSet);
        AppendFinancial(lines, bundle.Financial, metricSet);
        AppendRecovery(lines, bundle.Recovery, metricSet);
        AppendField(lines, bundle.Field, metricSet);
        AppendLegal(lines, bundle.Legal, metricSet);
        AppendEvidence(lines, bundle.Evidence, metricSet);
        AppendExposure(lines, bundle.Exposure, metricSet);
        AppendUnavailable(lines, bundle.UnavailableMetrics, metricSet);

        if (lines.Count == 0)
            return "No requested facts are available for this dealer in the configured period.";

        return string.Join(Environment.NewLine, lines);
    }

    public static string ComposeDepotFollowUp(BusinessChatConversationContext context)
        => $"This dealer is under depot {context.DepotCode}"
           + (string.IsNullOrWhiteSpace(context.DepotName) ? "." : $" ({context.DepotName}).");

    public static string ComposeDepotClarification(string dealerCode)
        => $"Please provide the depot code for dealer {dealerCode}.";

    private static void AppendIdentity(
        List<string> lines,
        DealerIdentityFacts? identity,
        HashSet<BusinessMetric> metrics)
    {
        if (identity is null)
            return;

        if (metrics.Contains(BusinessMetric.DealerName) || metrics.Contains(BusinessMetric.DealerCode))
            lines.Add($"Dealer {identity.DealerCode} — {identity.DealerName}.");

        if (metrics.Contains(BusinessMetric.DepotCode) || metrics.Contains(BusinessMetric.DepotName))
            lines.Add($"Depot {identity.DepotCode} — {identity.DepotName}.");

        if (metrics.Contains(BusinessMetric.Region) && !string.IsNullOrWhiteSpace(identity.DepotRegion))
            lines.Add($"Depot region: {identity.DepotRegion}.");

        if (metrics.Contains(BusinessMetric.Region) && !string.IsNullOrWhiteSpace(identity.RegionName))
            lines.Add($"Region name: {identity.RegionName}.");

        if (metrics.Contains(BusinessMetric.Territory))
        {
            if (!string.IsNullOrWhiteSpace(identity.TerritoryCode) || !string.IsNullOrWhiteSpace(identity.TerritoryName))
                lines.Add($"Territory: {FormatPair(identity.TerritoryCode, identity.TerritoryName)}.");
        }

        if (metrics.Contains(BusinessMetric.BillTo) && !string.IsNullOrWhiteSpace(identity.BillTo))
            lines.Add($"Bill To: {identity.BillTo}.");

        if (metrics.Contains(BusinessMetric.CustomerType) && !string.IsNullOrWhiteSpace(identity.CustomerType))
            lines.Add($"Customer type: {identity.CustomerType}.");

        if (metrics.Contains(BusinessMetric.MotherAccount) && !string.IsNullOrWhiteSpace(identity.MotherAccount))
            lines.Add($"Mother account: {identity.MotherAccount}.");
    }

    private static void AppendFinancial(
        List<string> lines,
        DealerFinancialFacts? financial,
        HashSet<BusinessMetric> metrics)
    {
        if (financial is null)
            return;

        var wantsAgeing = metrics.Contains(BusinessMetric.AgeingBucket0)
                          || metrics.Contains(BusinessMetric.AgeingBucket1)
                          || metrics.Contains(BusinessMetric.AgeingBucket2)
                          || metrics.Contains(BusinessMetric.AgeingBucket3)
                          || metrics.Contains(BusinessMetric.AgeingBucket4);

        if (metrics.Contains(BusinessMetric.CurrentOutstanding))
            lines.Add($"Current Outstanding: {FormatMoney(financial.CurrentOutstanding)} (period {financial.PeriodKey}).");

        if (metrics.Contains(BusinessMetric.Over90Outstanding))
            lines.Add($">90 Days Outstanding: {FormatMoney(financial.Over90Outstanding)}.");

        if (wantsAgeing)
        {
            lines.Add("Ageing breakup:");
            foreach (var (label, amount) in OdosOutstandingSemantics.BucketBreakdown(
                         financial.OutstandingBucket0,
                         financial.OutstandingBucket1,
                         financial.OutstandingBucket2,
                         financial.OutstandingBucket3,
                         financial.OutstandingBucket4))
            {
                lines.Add($"  {label}: {FormatMoney(amount)}");
            }
        }

        if (metrics.Contains(BusinessMetric.BusinessLineLimit) && financial.BusinessLineLimit is not null)
            lines.Add($"Business line limit: {FormatMoney(financial.BusinessLineLimit)}.");
    }

    private static void AppendRecovery(
        List<string> lines,
        DealerRecoveryFacts? recovery,
        HashSet<BusinessMetric> metrics)
    {
        if (recovery is null)
            return;

        if (metrics.Contains(BusinessMetric.NoticeGenerated) && !string.IsNullOrWhiteSpace(recovery.NoticeGeneratedYn))
            lines.Add($"Demand notice generated: {recovery.NoticeGeneratedYn}.");

        if (metrics.Contains(BusinessMetric.NoticeDepotStatus)
            && (!string.IsNullOrWhiteSpace(recovery.NoticeDepotYn) || recovery.NoticeDepotDate is not null))
            lines.Add(FormatNotice("Depot notice decision", recovery.NoticeDepotYn, recovery.NoticeDepotDate, recovery.NoticeDepotRemarks));

        if (metrics.Contains(BusinessMetric.NoticeHoStatus)
            && (!string.IsNullOrWhiteSpace(recovery.NoticeHoYn) || recovery.NoticeHoDate is not null))
            lines.Add(FormatNotice("HO notice decision", recovery.NoticeHoYn, recovery.NoticeHoDate, recovery.NoticeHoRemarks));

        if (metrics.Contains(BusinessMetric.NoticeDate) && recovery.NoticeHoDate is not null)
            lines.Add($"Notice date (HO): {recovery.NoticeHoDate:dd-MMM-yyyy}.");

        if (metrics.Contains(BusinessMetric.RecoveryStatus)
            && (!string.IsNullOrWhiteSpace(recovery.RecoveryStatusDescription) || !string.IsNullOrWhiteSpace(recovery.RecoveryStatusCode)))
            lines.Add(FormatStatus("Recovery status", recovery.RecoveryStatusCode, recovery.RecoveryStatusDescription));

        if (metrics.Contains(BusinessMetric.LegalStatus)
            && (!string.IsNullOrWhiteSpace(recovery.LegalStatusDescription) || !string.IsNullOrWhiteSpace(recovery.LegalStatusCode)))
            lines.Add(FormatStatus("Legal status", recovery.LegalStatusCode, recovery.LegalStatusDescription));
    }

    private static void AppendField(List<string> lines, DealerFieldFacts? field, HashSet<BusinessMetric> metrics)
    {
        if (field is null)
            return;

        if (metrics.Contains(BusinessMetric.PtpAmount) && field.PtpAmount is not null)
            lines.Add($"Latest PTP amount: {FormatMoney(field.PtpAmount)}.");
        if (metrics.Contains(BusinessMetric.PtpDate) && field.PtpDate is not null)
            lines.Add($"PTP promise date: {field.PtpDate:dd-MMM-yyyy}.");
        if (metrics.Contains(BusinessMetric.PtpStatus) && !string.IsNullOrWhiteSpace(field.PtpStatus))
            lines.Add($"PTP status: {field.PtpStatus}.");
        if (metrics.Contains(BusinessMetric.PtpConfidence) && field.PtpConfidence is not null)
            lines.Add($"PTP confidence: {field.PtpConfidence:P0}.");
        if (metrics.Contains(BusinessMetric.ChaseStatus) && !string.IsNullOrWhiteSpace(field.ChaseStatus))
            lines.Add($"PTP chase status: {field.ChaseStatus}.");
        if (metrics.Contains(BusinessMetric.LastVisitDate) && field.LastVisitDate is not null)
            lines.Add($"Last visit date: {field.LastVisitDate:dd-MMM-yyyy}.");
        if (metrics.Contains(BusinessMetric.VisitStatus) && !string.IsNullOrWhiteSpace(field.VisitStatus))
            lines.Add($"TSI visit status: {field.VisitStatus}.");
        if (metrics.Contains(BusinessMetric.TsiVisitCount) && field.TsiVisitCount is not null)
            lines.Add($"TSI visit count: {field.TsiVisitCount}.");
        if (metrics.Contains(BusinessMetric.DealerFeedback))
        {
            if (string.IsNullOrWhiteSpace(field.DealerFeedback))
                lines.Add("Dealer feedback: not available.");
            else
                lines.Add($"Dealer feedback: {field.DealerFeedback}.");
        }
        if (metrics.Contains(BusinessMetric.VisitOwner) && !string.IsNullOrWhiteSpace(field.VisitOwner))
            lines.Add($"Follow-up owner: {field.VisitOwner}.");
        if (metrics.Contains(BusinessMetric.VisitPlanStatus) && !string.IsNullOrWhiteSpace(field.VisitPlanStatus))
            lines.Add($"Visit plan status: {field.VisitPlanStatus}.");
    }

    private static void AppendLegal(List<string> lines, DealerLegalFacts? legal, HashSet<BusinessMetric> metrics)
    {
        if (legal is null)
            return;

        if (metrics.Contains(BusinessMetric.ChequeNumberMasked) && !string.IsNullOrWhiteSpace(legal.ChequeNumberMasked))
            lines.Add($"Security cheque: {legal.ChequeNumberMasked}.");
        if (metrics.Contains(BusinessMetric.ChequeAmount) && legal.ChequeAmount is not null)
            lines.Add($"Cheque amount: {FormatMoney(legal.ChequeAmount)}.");
        if (metrics.Contains(BusinessMetric.ChequeStatus) && !string.IsNullOrWhiteSpace(legal.ChequeStatus))
            lines.Add($"Cheque status: {legal.ChequeStatus}.");
        if (metrics.Contains(BusinessMetric.ReturnMemoReason))
        {
            if (legal.ReturnMemoAvailability == "NOT_AVAILABLE")
                lines.Add("Return memo reason: not available.");
            else if (!string.IsNullOrWhiteSpace(legal.ReturnMemoReason))
                lines.Add($"Return memo reason: {legal.ReturnMemoReason}.");
        }
        if (metrics.Contains(BusinessMetric.Section138Eligibility))
        {
            if (legal.Section138Eligible is null)
                lines.Add("Section 138 eligibility: not established.");
            else
                lines.Add($"Section 138 eligible: {(legal.Section138Eligible.Value ? "Yes" : "No")}.");
        }
        if (metrics.Contains(BusinessMetric.EligibilityReason) && !string.IsNullOrWhiteSpace(legal.EligibilityReason))
            lines.Add($"Eligibility: {legal.EligibilityReason}.");
        if (metrics.Contains(BusinessMetric.LimitationDaysRemaining) && legal.LimitationDaysRemaining is not null)
            lines.Add($"Limitation days remaining: {legal.LimitationDaysRemaining}.");
        if (metrics.Contains(BusinessMetric.NoticeByDate) && legal.NoticeByDate is not null)
            lines.Add($"Notice-by date: {legal.NoticeByDate:dd-MMM-yyyy} (LEGAL_TBC).");
        if (metrics.Contains(BusinessMetric.CureByDate) && legal.CureByDate is not null)
            lines.Add($"Cure-by date: {legal.CureByDate:dd-MMM-yyyy} (LEGAL_TBC).");
        if (metrics.Contains(BusinessMetric.FileByDate) && legal.FileByDate is not null)
            lines.Add($"File-by date: {legal.FileByDate:dd-MMM-yyyy} (LEGAL_TBC).");
        if (metrics.Contains(BusinessMetric.LegalDeadlineStatus) && !string.IsNullOrWhiteSpace(legal.LegalDeadlineStatus))
            lines.Add($"Legal deadline status: {legal.LegalDeadlineStatus}.");
    }

    private static void AppendEvidence(List<string> lines, DealerEvidenceFacts? evidence, HashSet<BusinessMetric> metrics)
    {
        if (evidence is null)
            return;

        if (metrics.Contains(BusinessMetric.EvidenceCompleteness) && evidence.CompletenessScore is not null)
            lines.Add($"Evidence completeness: {evidence.CompletenessScore:P0}.");
        if (metrics.Contains(BusinessMetric.MissingEvidence) && evidence.MissingEvidence.Count > 0)
            lines.Add($"Missing evidence: {string.Join(", ", evidence.MissingEvidence)}.");
        if (metrics.Contains(BusinessMetric.LegalDocumentStatus) && !string.IsNullOrWhiteSpace(evidence.LegalDocumentStatus))
            lines.Add($"Case file status: {evidence.LegalDocumentStatus}.");
    }

    private static void AppendExposure(List<string> lines, DealerExposureFacts? exposure, HashSet<BusinessMetric> metrics)
    {
        if (exposure is null)
            return;

        if (metrics.Contains(BusinessMetric.GrossOpenAr) && exposure.GrossOpenAr is not null)
            lines.Add($"Gross open AR: {FormatMoney(exposure.GrossOpenAr)}.");
        if (metrics.Contains(BusinessMetric.NetRecoverableExposure))
        {
            if (exposure.NetRecoverableExposure is not null)
                lines.Add($"Net recoverable exposure: {FormatMoney(exposure.NetRecoverableExposure)}.");
            else
                lines.Add("Net recoverable exposure: not available — required adjustment components are missing.");
        }
        if (metrics.Contains(BusinessMetric.ExposureMissingComponents) && exposure.MissingComponents.Count > 0)
            lines.Add($"Exposure gaps: {string.Join(", ", exposure.MissingComponents)}.");
    }

    private static void AppendUnavailable(
        List<string> lines,
        IReadOnlyList<MetricUnavailability>? unavailable,
        HashSet<BusinessMetric> metrics)
    {
        if (unavailable is null)
            return;
        foreach (var item in unavailable.Where(u => metrics.Contains(u.Metric)))
            lines.Add($"{FormatMetricLabel(item.Metric)}: not available ({FormatUnavailableReason(item.Reason)})");
    }

    private static string FormatUnavailableReason(string reason)
        => reason switch
        {
            BusinessChatUnavailabilityCodes.OdosSessionNotConfigured =>
                "Outstanding information is temporarily unavailable for the selected operational period.",
            BusinessChatUnavailabilityCodes.FieldPersistenceNotConfigured =>
                "PTP and chase information is not configured for this environment.",
            _ => reason
        };

    private static string FormatMetricLabel(BusinessMetric metric)
        => metric switch
        {
            BusinessMetric.ReturnMemoReason => "Return memo",
            BusinessMetric.NetRecoverableExposure => "Net recoverable exposure",
            BusinessMetric.LastVisitDate => "Last visit",
            BusinessMetric.TsiVisitCount => "TSI visit count",
            BusinessMetric.DealerFeedback => "Dealer feedback",
            BusinessMetric.VisitPlanStatus => "Visit plan",
            _ => metric.ToString()
        };

    private static string FormatNotice(string label, string? decision, DateOnly? date, string? remarks)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(decision))
            parts.Add(decision);
        if (date is not null)
            parts.Add(date.Value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(remarks))
            parts.Add(remarks);
        return parts.Count == 0 ? $"{label}: not available." : $"{label}: {string.Join(" — ", parts)}.";
    }

    private static string FormatStatus(string label, string? code, string? description)
    {
        if (!string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(code))
            return $"{label}: {code} ({description}).";
        if (!string.IsNullOrWhiteSpace(description))
            return $"{label}: {description}.";
        if (!string.IsNullOrWhiteSpace(code))
            return $"{label}: {code}.";
        return $"{label}: not available.";
    }

    private static string FormatMoney(decimal? amount)
        => amount is null ? "not available" : $"₹{amount.Value.ToString("N2", InrCulture)}";

    private static string FormatPair(string? code, string? name)
    {
        if (string.IsNullOrWhiteSpace(code))
            return name ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return code;
        return $"{code} ({name})";
    }
}

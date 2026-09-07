namespace ARC.Domain.BusinessChat;

/// <summary>
/// Versioned code-first catalog of approved business metrics, capabilities, and phrase aliases.
/// Adding a new phrasing requires only an alias entry here — not new classes or SPs.
/// </summary>
public static class BusinessMetricCatalog
{
    public const string Version = "2.0.0";

    private static readonly IReadOnlyDictionary<BusinessMetric, BusinessMetricDefinition> Definitions =
        BuildDefinitions().ToDictionary(d => d.Metric);

    private static readonly IReadOnlyList<AliasEntry> Aliases = BuildAliases();

    public static IReadOnlyCollection<BusinessMetric> AllMetrics => Definitions.Keys.ToList();

    public static BusinessCapability GetCapability(BusinessMetric metric)
        => Definitions[metric].Capability;

    public static IReadOnlyList<BusinessMetric> MetricsForCapability(BusinessCapability capability)
        => Definitions.Values
            .Where(d => d.Capability == capability)
            .Select(d => d.Metric)
            .ToList();

    public static IReadOnlyList<BusinessMetric> AllSummaryMetrics()
        => Definitions.Keys.ToList();

    public static bool IsKnownMetric(BusinessMetric metric) => Definitions.ContainsKey(metric);

    public static bool IsKnownCapability(BusinessCapability capability)
        => Definitions.Values.Any(d => d.Capability == capability);

    /// <summary>Matches metric aliases in message text. Longest alias wins per position.</summary>
    public static IReadOnlyList<BusinessMetric> MatchMetrics(string message)
    {
        var text = message.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var matched = new HashSet<BusinessMetric>();
        ApplyMultiMetricPhrases(text, matched);

        foreach (var alias in Aliases.OrderByDescending(a => a.Phrase.Length))
        {
            if (text.Contains(alias.Phrase, StringComparison.OrdinalIgnoreCase))
                matched.Add(alias.Metric);
        }

        if (IsFullSummaryRequest(text))
        {
            foreach (var metric in AllSummaryMetrics())
                matched.Add(metric);
        }

        return matched.OrderBy(m => (int)m).ToList();
    }

    public static bool IsFullSummaryRequest(string message)
    {
        var text = message.Trim();
        return ContainsAny(text,
            "complete outstanding details",
            "full details",
            "all details",
            "give me a summary",
            "dealer summary",
            "complete details",
            "summary of this dealer",
            "outstanding and ageing and notice",
            "recovery summary",
            "overall recovery",
            "overall summary",
            "complete recovery summary",
            "outstanding, ptp and notice",
            "outstanding and ptp",
            "latest visit and legal");
    }

    private static void ApplyMultiMetricPhrases(string text, HashSet<BusinessMetric> matched)
    {
        foreach (var (phrase, metrics) in MultiMetricPhrases.OrderByDescending(p => p.Phrase.Length))
        {
            if (!text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var metric in metrics)
                matched.Add(metric);
        }
    }

    private static readonly IReadOnlyList<MultiMetricPhrase> MultiMetricPhrases =
    [
        new("outstanding and notice status", [BusinessMetric.CurrentOutstanding, BusinessMetric.NoticeGenerated]),
        new("outstanding and notice", [BusinessMetric.CurrentOutstanding, BusinessMetric.NoticeGenerated]),
        new("outstanding and legal status", [BusinessMetric.CurrentOutstanding, BusinessMetric.LegalStatus]),
        new("outstanding and legal", [BusinessMetric.CurrentOutstanding, BusinessMetric.LegalStatus]),
        new("ageing and notice", [BusinessMetric.AgeingBucket0, BusinessMetric.NoticeGenerated]),
        new("aging and notice", [BusinessMetric.AgeingBucket0, BusinessMetric.NoticeGenerated]),
    ];

    [Obsolete("Use IsFullSummaryRequest")]
    public static bool IsSummaryRequest(string message) => IsFullSummaryRequest(message);

    /// <summary>Legacy guard — Wave 2 routes exposure metrics to DealerFinancialAdjustments.</summary>
    public static bool IsUnsupportedExposureRequest(string message) => false;

    /// <summary>Legacy guard — Wave 2 routes eligibility metrics to DealerChequeAndLegal.</summary>
    public static bool IsUnsupportedEligibilityRequest(string message) => false;

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static IEnumerable<BusinessMetricDefinition> BuildDefinitions() =>
    [
        Def(BusinessMetric.DealerName, BusinessCapability.DealerIdentity),
        Def(BusinessMetric.DealerCode, BusinessCapability.DealerIdentity),
        Def(BusinessMetric.DepotCode, BusinessCapability.DealerIdentity),
        Def(BusinessMetric.DepotName, BusinessCapability.DealerIdentity),
        Def(BusinessMetric.Region, BusinessCapability.DealerIdentity),
        Def(BusinessMetric.Territory, BusinessCapability.DealerIdentity),
        Def(BusinessMetric.BillTo, BusinessCapability.DealerIdentity),
        Def(BusinessMetric.CustomerType, BusinessCapability.DealerIdentity),
        Def(BusinessMetric.MotherAccount, BusinessCapability.DealerIdentity),

        Def(BusinessMetric.CurrentOutstanding, BusinessCapability.DealerOutstanding),
        Def(BusinessMetric.AgeingBucket0, BusinessCapability.DealerOutstanding),
        Def(BusinessMetric.AgeingBucket1, BusinessCapability.DealerOutstanding),
        Def(BusinessMetric.AgeingBucket2, BusinessCapability.DealerOutstanding),
        Def(BusinessMetric.AgeingBucket3, BusinessCapability.DealerOutstanding),
        Def(BusinessMetric.AgeingBucket4, BusinessCapability.DealerOutstanding),
        Def(BusinessMetric.Over90Outstanding, BusinessCapability.DealerOutstanding),
        Def(BusinessMetric.BusinessLineLimit, BusinessCapability.DealerOutstanding),

        Def(BusinessMetric.NoticeGenerated, BusinessCapability.DealerRecoveryStatus),
        Def(BusinessMetric.NoticeDepotStatus, BusinessCapability.DealerRecoveryStatus),
        Def(BusinessMetric.NoticeHoStatus, BusinessCapability.DealerRecoveryStatus),
        Def(BusinessMetric.NoticeDate, BusinessCapability.DealerRecoveryStatus),
        Def(BusinessMetric.RecoveryStatus, BusinessCapability.DealerRecoveryStatus),
        Def(BusinessMetric.LegalStatus, BusinessCapability.DealerRecoveryStatus),

        Def(BusinessMetric.PtpAmount, BusinessCapability.DealerVisitAndPtp),
        Def(BusinessMetric.PtpDate, BusinessCapability.DealerVisitAndPtp),
        Def(BusinessMetric.PtpStatus, BusinessCapability.DealerVisitAndPtp),
        Def(BusinessMetric.PtpConfidence, BusinessCapability.DealerVisitAndPtp),
        Def(BusinessMetric.ChaseStatus, BusinessCapability.DealerVisitAndPtp),
        Def(BusinessMetric.LastVisitDate, BusinessCapability.DealerVisitAndPtp),
        Def(BusinessMetric.VisitOwner, BusinessCapability.DealerVisitAndPtp),
        Def(BusinessMetric.VisitPlanStatus, BusinessCapability.DealerVisitAndPtp),

        Def(BusinessMetric.VisitStatus, BusinessCapability.DealerOdosVisitAggregate),
        Def(BusinessMetric.TsiVisitCount, BusinessCapability.DealerOdosVisitAggregate),
        Def(BusinessMetric.DealerFeedback, BusinessCapability.DealerOdosVisitAggregate),

        Def(BusinessMetric.ChequeNumberMasked, BusinessCapability.DealerChequeAndLegal),
        Def(BusinessMetric.ChequeDate, BusinessCapability.DealerChequeAndLegal),
        Def(BusinessMetric.ChequeAmount, BusinessCapability.DealerChequeAndLegal),
        Def(BusinessMetric.ChequeStatus, BusinessCapability.DealerChequeAndLegal),
        Def(BusinessMetric.ReturnMemoReason, BusinessCapability.DealerChequeAndLegal),
        Def(BusinessMetric.Section138Eligibility, BusinessCapability.DealerChequeAndLegal),
        Def(BusinessMetric.EligibilityReason, BusinessCapability.DealerChequeAndLegal),
        Def(BusinessMetric.LimitationDaysRemaining, BusinessCapability.DealerChequeAndLegal),
        Def(BusinessMetric.NoticeByDate, BusinessCapability.DealerChequeAndLegal),
        Def(BusinessMetric.CureByDate, BusinessCapability.DealerChequeAndLegal),
        Def(BusinessMetric.FileByDate, BusinessCapability.DealerChequeAndLegal),
        Def(BusinessMetric.LegalDeadlineStatus, BusinessCapability.DealerChequeAndLegal),

        Def(BusinessMetric.EvidenceCompleteness, BusinessCapability.DealerEvidenceStatus),
        Def(BusinessMetric.MissingEvidence, BusinessCapability.DealerEvidenceStatus),
        Def(BusinessMetric.LegalDocumentStatus, BusinessCapability.DealerEvidenceStatus),

        Def(BusinessMetric.NetRecoverableExposure, BusinessCapability.DealerFinancialAdjustments),
        Def(BusinessMetric.ExposureMissingComponents, BusinessCapability.DealerFinancialAdjustments),
        Def(BusinessMetric.GrossOpenAr, BusinessCapability.DealerFinancialAdjustments),
    ];

    private static BusinessMetricDefinition Def(BusinessMetric metric, BusinessCapability capability)
        => new(metric, capability);

    private static IReadOnlyList<AliasEntry> BuildAliases() =>
    [
        // Identity
        Alias("dealer name", BusinessMetric.DealerName),
        Alias("dealer details", BusinessMetric.DealerName),
        Alias("show dealer", BusinessMetric.DealerName),
        Alias("dealer master", BusinessMetric.DealerName),
        Alias("dealer code", BusinessMetric.DealerCode),
        Alias("depot code", BusinessMetric.DepotCode),
        Alias("depot name", BusinessMetric.DepotName),
        Alias("which depot", BusinessMetric.DepotCode),
        Alias("region", BusinessMetric.Region),
        Alias("territory", BusinessMetric.Territory),
        Alias("bill to", BusinessMetric.BillTo),
        Alias("customer type", BusinessMetric.CustomerType),
        Alias("mother account", BusinessMetric.MotherAccount),

        // Outstanding
        Alias("outstanding", BusinessMetric.CurrentOutstanding),
        Alias("current os", BusinessMetric.CurrentOutstanding),
        Alias("show os", BusinessMetric.CurrentOutstanding),
        Alias("what is os", BusinessMetric.CurrentOutstanding),
        Alias("how much is due", BusinessMetric.CurrentOutstanding),
        Alias("how much due", BusinessMetric.CurrentOutstanding),
        Alias("amount due", BusinessMetric.CurrentOutstanding),
        Alias("current outstanding", BusinessMetric.CurrentOutstanding),
        Alias("odos details", BusinessMetric.CurrentOutstanding),
        Alias("his outstanding", BusinessMetric.CurrentOutstanding),
        Alias("her outstanding", BusinessMetric.CurrentOutstanding),
        Alias("their outstanding", BusinessMetric.CurrentOutstanding),

        Alias("ageing", BusinessMetric.AgeingBucket0),
        Alias("aging", BusinessMetric.AgeingBucket0),
        Alias("ageing breakup", BusinessMetric.AgeingBucket0),
        Alias("breakup", BusinessMetric.AgeingBucket0),
        Alias("break up", BusinessMetric.AgeingBucket0),
        Alias("how old is the outstanding", BusinessMetric.AgeingBucket0),

        Alias("over 90", BusinessMetric.Over90Outstanding),
        Alias("above 90", BusinessMetric.Over90Outstanding),
        Alias(">90", BusinessMetric.Over90Outstanding),
        Alias("more than 90", BusinessMetric.Over90Outstanding),
        Alias("overdue more than 90", BusinessMetric.Over90Outstanding),

        Alias("business line limit", BusinessMetric.BusinessLineLimit),
        Alias("sbl limit", BusinessMetric.BusinessLineLimit),

        // Recovery
        Alias("notice status", BusinessMetric.NoticeGenerated),
        Alias("notice generated", BusinessMetric.NoticeGenerated),
        Alias("demand notice", BusinessMetric.NoticeGenerated),
        Alias("has notice", BusinessMetric.NoticeGenerated),
        Alias("any notice", BusinessMetric.NoticeGenerated),
        Alias("been generated", BusinessMetric.NoticeGenerated),
        Alias("notice date", BusinessMetric.NoticeDate),
        Alias("recovery status", BusinessMetric.RecoveryStatus),
        Alias("odos status", BusinessMetric.RecoveryStatus),
        Alias("legal status", BusinessMetric.LegalStatus),
        Alias("case status", BusinessMetric.RecoveryStatus),
        Alias("legal outstanding status", BusinessMetric.RecoveryStatus),

        // PTP / visit
        Alias("ptp", BusinessMetric.PtpAmount),
        Alias("promise to pay", BusinessMetric.PtpAmount),
        Alias("any promise to pay", BusinessMetric.PtpAmount),
        Alias("promised payment", BusinessMetric.PtpAmount),
        Alias("dealer committed", BusinessMetric.PtpAmount),
        Alias("dealer committed anything", BusinessMetric.PtpAmount),
        Alias("latest ptp", BusinessMetric.PtpAmount),
        Alias("what amount was promised", BusinessMetric.PtpAmount),
        Alias("when is payment promised", BusinessMetric.PtpDate),
        Alias("promise date", BusinessMetric.PtpDate),
        Alias("ptp status", BusinessMetric.PtpStatus),
        Alias("ptp broken", BusinessMetric.ChaseStatus),
        Alias("broken ptp", BusinessMetric.ChaseStatus),
        Alias("has the ptp been broken", BusinessMetric.ChaseStatus),
        Alias("chase status", BusinessMetric.ChaseStatus),
        Alias("show chase status", BusinessMetric.ChaseStatus),
        Alias("last visit", BusinessMetric.LastVisitDate),
        Alias("latest recovery visit", BusinessMetric.LastVisitDate),
        Alias("latest visit", BusinessMetric.LastVisitDate),
        Alias("what was the last visit", BusinessMetric.LastVisitDate),
        Alias("visit planned", BusinessMetric.VisitPlanStatus),
        Alias("is there a visit planned", BusinessMetric.VisitPlanStatus),
        Alias("visit plan", BusinessMetric.VisitPlanStatus),
        Alias("who owns the next follow-up", BusinessMetric.VisitOwner),
        Alias("tsi visited", BusinessMetric.VisitStatus),
        Alias("has tsi visited", BusinessMetric.VisitStatus),
        Alias("any tsi visit", BusinessMetric.VisitStatus),
        Alias("how many tsi visits", BusinessMetric.TsiVisitCount),
        Alias("tsi visits are recorded", BusinessMetric.TsiVisitCount),
        Alias("visit count", BusinessMetric.TsiVisitCount),
        Alias("show visit count", BusinessMetric.TsiVisitCount),
        Alias("dealer feedback", BusinessMetric.DealerFeedback),
        Alias("what is the dealer feedback", BusinessMetric.DealerFeedback),
        Alias("visit and ptp", BusinessMetric.PtpStatus),
        Alias("show visit and ptp", BusinessMetric.PtpStatus),

        // Cheque
        Alias("cheque details", BusinessMetric.ChequeAmount),
        Alias("show cheque", BusinessMetric.ChequeAmount),
        Alias("what about cheque", BusinessMetric.ChequeAmount),
        Alias("about cheque", BusinessMetric.ChequeAmount),
        Alias("any cheque", BusinessMetric.ChequeAmount),
        Alias("what cheque", BusinessMetric.ChequeAmount),
        Alias("cheque available", BusinessMetric.ChequeNumberMasked),
        Alias("what cheque is available", BusinessMetric.ChequeNumberMasked),
        Alias("security cheque", BusinessMetric.ChequeAmount),
        Alias("which security cheque", BusinessMetric.ChequeNumberMasked),
        Alias("latest cheque", BusinessMetric.ChequeAmount),
        Alias("cheque amount", BusinessMetric.ChequeAmount),
        Alias("cheque bounced", BusinessMetric.ChequeStatus),
        Alias("did cheque bounce", BusinessMetric.ChequeStatus),
        Alias("has a cheque bounced", BusinessMetric.ChequeStatus),
        Alias("has the cheque bounced", BusinessMetric.ChequeStatus),
        Alias("any return memo", BusinessMetric.ReturnMemoReason),
        Alias("bounce", BusinessMetric.ReturnMemoReason),
        Alias("return memo", BusinessMetric.ReturnMemoReason),

        // Legal / Section 138 / limitation
        Alias("section 138", BusinessMetric.Section138Eligibility),
        Alias("s138", BusinessMetric.Section138Eligibility),
        Alias("legal eligible", BusinessMetric.Section138Eligibility),
        Alias("can section 138 proceed", BusinessMetric.Section138Eligibility),
        Alias("can 138 proceed", BusinessMetric.Section138Eligibility),
        Alias("can legal proceed", BusinessMetric.Section138Eligibility),
        Alias("legal proceed", BusinessMetric.Section138Eligibility),
        Alias("is legal action possible", BusinessMetric.Section138Eligibility),
        Alias("legal action possible", BusinessMetric.Section138Eligibility),
        Alias("eligible for section 138", BusinessMetric.Section138Eligibility),
        Alias("is this dealer eligible", BusinessMetric.Section138Eligibility),
        Alias("why is legal action blocked", BusinessMetric.EligibilityReason),
        Alias("what facts are missing for section 138", BusinessMetric.EligibilityReason),
        Alias("limitation status", BusinessMetric.LegalDeadlineStatus),
        Alias("what is limitation status", BusinessMetric.LegalDeadlineStatus),
        Alias("limitation date", BusinessMetric.FileByDate),
        Alias("filing deadline", BusinessMetric.FileByDate),
        Alias("file-by date", BusinessMetric.FileByDate),
        Alias("file by date", BusinessMetric.FileByDate),
        Alias("how many days are left", BusinessMetric.LimitationDaysRemaining),
        Alias("days remaining", BusinessMetric.LimitationDaysRemaining),
        Alias("statutory deadlines", BusinessMetric.LegalDeadlineStatus),
        Alias("legal deadline", BusinessMetric.LegalDeadlineStatus),
        Alias("deadline close", BusinessMetric.LegalDeadlineStatus),
        Alias("cure by date", BusinessMetric.CureByDate),
        Alias("notice by date", BusinessMetric.NoticeByDate),

        // Evidence
        Alias("evidence complete", BusinessMetric.EvidenceCompleteness),
        Alias("evidence completeness", BusinessMetric.EvidenceCompleteness),
        Alias("case file complete", BusinessMetric.EvidenceCompleteness),
        Alias("is the case file complete", BusinessMetric.EvidenceCompleteness),
        Alias("missing evidence", BusinessMetric.MissingEvidence),
        Alias("missing documents", BusinessMetric.MissingEvidence),
        Alias("what evidence is missing", BusinessMetric.MissingEvidence),
        Alias("cheque scan available", BusinessMetric.LegalDocumentStatus),
        Alias("return memo available", BusinessMetric.LegalDocumentStatus),
        Alias("notice pod available", BusinessMetric.LegalDocumentStatus),

        // Net exposure
        Alias("net exposure", BusinessMetric.NetRecoverableExposure),
        Alias("net recoverable", BusinessMetric.NetRecoverableExposure),
        Alias("net recoverable exposure", BusinessMetric.NetRecoverableExposure),
        Alias("compute exposure", BusinessMetric.NetRecoverableExposure),
        Alias("how much is actually recoverable", BusinessMetric.NetRecoverableExposure),
        Alias("actually recoverable", BusinessMetric.NetRecoverableExposure),
        Alias("credit notes", BusinessMetric.ExposureMissingComponents),
        Alias("rebates adjustment", BusinessMetric.ExposureMissingComponents),
        Alias("disputes adjustment", BusinessMetric.ExposureMissingComponents),
    ];

    private static AliasEntry Alias(string phrase, BusinessMetric metric) => new(phrase, metric);

    private sealed record BusinessMetricDefinition(BusinessMetric Metric, BusinessCapability Capability);

    private sealed record AliasEntry(string Phrase, BusinessMetric Metric);

    private sealed record MultiMetricPhrase(string Phrase, IReadOnlyList<BusinessMetric> Metrics);
}

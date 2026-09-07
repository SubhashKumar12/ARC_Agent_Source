using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;

namespace ARC.Domain.Metrics;

/// <summary>
/// Interim A2 score identifier. This is A1 net recoverable exposure, not the
/// assignment composite SP3 recoverability model.
/// </summary>
public static class RecoverabilityScoreFormula
{
    public const string Id = MetricContract.Version;

    public const string Description =
        "Interim score equals net recoverable exposure. This is not the assignment composite SP3 recoverability model.";
}

/// <summary>Explicit availability of one assignment input. No completeness percentage.</summary>
public enum A2InputAvailability
{
    Available = 0,
    Unavailable = 1,
    Tbc = 2,
    SyntheticOnly = 3,
    UnverifiedProduction = 4
}

public sealed record A2InputStatus(string Input, A2InputAvailability Status, string? Detail = null);

public sealed record A2TbcIndicator(string Id, string Status, string Detail);

public sealed record A2LineageRef(
    string SourceSystem,
    string SourceTable,
    string SourceKey,
    decimal Amount,
    DateOnly PostedOn);

public sealed record A2ScoreProvenance(
    bool Complete,
    string Status,
    string ScoreOrigin,
    string ScoreFormula,
    IReadOnlyList<A2LineageRef> Lineage)
{
    public static A2ScoreProvenance Incomplete(string scoreFormula) => new(
        false,
        "Incomplete",
        "NetRecoverableExposure",
        scoreFormula,
        []);

    public static A2ScoreProvenance FromLineage(IReadOnlyList<LineItemRef>? lineage, string scoreFormula)
    {
        if (lineage is not { Count: > 0 })
            return Incomplete(scoreFormula);

        var refs = lineage.Select(l => new A2LineageRef(
            l.SourceSystem,
            l.SourceTable,
            l.SourceKey,
            l.Amount,
            l.PostedOn)).ToList();

        return new A2ScoreProvenance(
            true,
            "Complete",
            "NetRecoverableExposure",
            scoreFormula,
            refs);
    }
}

/// <summary>
/// Chat/API-safe A2 view. Score and tier are copied from the deterministic tool.
/// Formula, TBC flags, completeness and provenance are server-generated and
/// cannot be supplied by a caller or by LLM narration.
/// </summary>
public sealed record A2ChatSafeContract(
    string DealerUrn,
    decimal RecoverabilityScore,
    string ScoreFormula,
    int? Rank,
    string Tier,
    bool? IsTopDecile,
    string DeterministicExplanation,
    A2ScoreProvenance Provenance,
    IReadOnlyList<A2InputStatus> DataCompleteness,
    IReadOnlyList<A2TbcIndicator> TbcIndicators);

/// <summary>Facts the assembler may observe. It never accepts formula, TBC, or provenance from a caller.</summary>
public sealed record A2ChatSafeFacts(
    string DealerUrn,
    decimal RecoverabilityScore,
    RecoveryTier Tier,
    int? Rank = null,
    bool? IsTopDecile = null,
    ExposureBreakdown? Exposure = null,
    bool ChequeBounceKnown = false,
    bool DemandNoticePresent = false,
    bool TsiRemarksPresent = false,
    bool VisitCutoffConfigured = false,
    bool OutstandingKnown = false,
    bool AgeingKnown = false,
    bool PaymentHistoryPresent = false,
    bool GraphFeaturesUsedInScore = false,
    bool VisitHistoryPresent = false,
    bool DealerIdentityPresent = true);

/// <summary>
/// Builds the chat-safe contract. Completeness and TBC catalogs are central and deterministic.
/// </summary>
public static class A2ChatSafeAssembler
{
    public static A2ChatSafeContract Assemble(A2ChatSafeFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (string.IsNullOrWhiteSpace(facts.DealerUrn))
            throw new ArgumentException("DealerUrn is required.", nameof(facts));

        var formula = RecoverabilityScoreFormula.Id;
        var provenance = A2ScoreProvenance.FromLineage(facts.Exposure?.Lineage, formula);
        var completeness = BuildCompleteness(facts);
        var tbc = BuildTbcIndicators(facts.VisitCutoffConfigured);
        var explanation = BuildExplanation(facts, provenance);

        return new A2ChatSafeContract(
            facts.DealerUrn,
            facts.RecoverabilityScore,
            formula,
            facts.Rank,
            facts.Tier.ToString(),
            facts.IsTopDecile,
            explanation,
            provenance,
            completeness,
            tbc);
    }

    public static A2ChatSafeContract FromPrioritisation(
        ExposureBreakdown exposure,
        RiskAssessment assessment,
        bool chequeBounceKnown,
        bool demandNoticePresent,
        bool tsiRemarksPresent,
        bool visitCutoffConfigured)
    {
        ArgumentNullException.ThrowIfNull(exposure);
        ArgumentNullException.ThrowIfNull(assessment);

        var score = assessment.Score ?? exposure.NetRecoverableExposure.Amount;
        return Assemble(new A2ChatSafeFacts(
            exposure.DealerUrn.Value,
            score,
            assessment.Tier,
            Exposure: exposure,
            ChequeBounceKnown: chequeBounceKnown,
            DemandNoticePresent: demandNoticePresent,
            TsiRemarksPresent: tsiRemarksPresent,
            VisitCutoffConfigured: visitCutoffConfigured,
            DealerIdentityPresent: true));
    }

    public static A2ChatSafeContract FromWorklistEntry(
        string dealerUrn,
        decimal recoverabilityScore,
        string recoveryTier,
        int rank,
        bool? isTopDecile,
        bool visitCutoffConfigured)
    {
        if (!Enum.TryParse<RecoveryTier>(recoveryTier, ignoreCase: true, out var tier))
            tier = RecoveryTier.Notice;

        return Assemble(new A2ChatSafeFacts(
            dealerUrn,
            recoverabilityScore,
            tier,
            Rank: rank,
            IsTopDecile: isTopDecile,
            VisitCutoffConfigured: visitCutoffConfigured,
            DealerIdentityPresent: !string.IsNullOrWhiteSpace(dealerUrn)));
    }

    public static IReadOnlyList<A2TbcIndicator> CentralTbcCatalog(bool visitCutoffConfigured)
        => BuildTbcIndicators(visitCutoffConfigured);

    private static IReadOnlyList<A2InputStatus> BuildCompleteness(A2ChatSafeFacts facts)
        =>
        [
            new("NetRecoverableExposure", A2InputAvailability.Available,
                "Interim score is copied from A1 NetRecoverableExposure."),
            new("Outstanding", facts.OutstandingKnown ? A2InputAvailability.Available : A2InputAvailability.Unavailable,
                "ODOS od_os_amt_updt is not an A2 score input and is not on ExposureBreakdown."),
            new("Ageing", facts.AgeingKnown ? A2InputAvailability.Available : A2InputAvailability.Unavailable,
                "OS0–OS4 amounts are not consumed by A2. Day-range meaning is TBC."),
            new("PaymentHistory", facts.PaymentHistoryPresent ? A2InputAvailability.SyntheticOnly : A2InputAvailability.Unavailable,
                "Payment history is not an A2 score input. Assignment feature remains TBC."),
            new("GraphFeatures", facts.GraphFeaturesUsedInScore ? A2InputAvailability.Available : A2InputAvailability.Tbc,
                "Graph features are not included in the interim score. Feature definition is TBC."),
            new("TsiRemarks", facts.TsiRemarksPresent ? A2InputAvailability.Available : A2InputAvailability.Unavailable,
                "Remarks may be narrated only. They do not change score or tier."),
            new("VisitHistory", facts.VisitHistoryPresent ? A2InputAvailability.Available : A2InputAvailability.Unavailable,
                "Visit history is not an A2 score input."),
            new("DealerIdentity", facts.DealerIdentityPresent ? A2InputAvailability.Available : A2InputAvailability.Unavailable,
                "Canonical URN is present on the case. URN ↔ od_dealer_code mapping remains TBC."),
            new("ChequeBounce", facts.ChequeBounceKnown ? A2InputAvailability.UnverifiedProduction : A2InputAvailability.Unavailable,
                "Used for Section138 tier only. Production cheque SP is not approved."),
            new("DemandNotice", facts.DemandNoticePresent ? A2InputAvailability.Available : A2InputAvailability.Unavailable,
                "A2 uses a host-supplied demand notice when present; notice history is not queried.")
        ];

    private static IReadOnlyList<A2TbcIndicator> BuildTbcIndicators(bool visitCutoffConfigured)
        =>
        [
            new("RecoverabilityFormula", "Tbc",
                "Assignment composite recoverability formula is not confirmed. Interim formula is net_recoverable_exposure.v1."),
            new("GraphFeatureDefinition", "Tbc",
                "Which graph features enter scoring versus explanation is not confirmed. They are not in the interim score."),
            new("TsiRemarkContribution", "Tbc",
                "Remarks are narration-only. Any effect on score or tier would require an explicit trainer rule."),
            new("VisitTierCutoff", "Tbc",
                visitCutoffConfigured
                    ? "A runtime VisitMaxNetExposure is configured, but the assignment cutoff remains unconfirmed."
                    : "VisitMaxNetExposure is null. Visit is not auto-assigned. Assignment cutoff is TBC."),
            new("AgeingBucketMeaning", "Tbc",
                "od_os_amt0–od_os_amt4 amounts may exist as ODOS facts; day-range meaning is not confirmed and is unused by A2."),
            new("EntryThresholdRelationship", "Tbc",
                "Relationship between 90-day / ₹5,000, business-line limit, and A2 score is not confirmed. The business-line limit is not the recoverability score."),
            new("DealerAggregationRule", "Tbc",
                "How opening transaction rows fold to one dealer ranking input is not confirmed."),
            new("DealerIdentityMapping", "Tbc",
                "Canonical URN ↔ od_dealer_code mapping is not an approved production contract."),
            new("TopDecilePopulation", "Tbc",
                "Whether top-decile is of cycle-ranked cases, ODOS-eligible dealers, or a TSI book is not confirmed. Current code uses the ranked cycle index.")
        ];

    private static string BuildExplanation(A2ChatSafeFacts facts, A2ScoreProvenance provenance)
    {
        var visit = facts.VisitCutoffConfigured
            ? "A runtime visit cutoff is configured; assignment confirmation of that cutoff is still TBC."
            : "Visit-tier cutoff is TBC because VisitMaxNetExposure is unset.";

        var remarks = facts.TsiRemarksPresent
            ? "TSI remarks were supplied for narration only and did not change the score or tier."
            : "TSI remarks were not supplied.";

        var lineage = provenance.Complete
            ? "Score provenance points at A1 lineage source references."
            : "Score provenance is incomplete because A1 lineage is unavailable; no source references were fabricated.";

        return
            "Interim score equals net recoverable exposure (" + RecoverabilityScoreFormula.Id + "). " +
            "This is not the assignment composite SP3 recoverability model. " +
            "Payment history, graph features and TSI remarks are not included in the score. " +
            visit + " " +
            remarks + " " +
            lineage;
    }
}

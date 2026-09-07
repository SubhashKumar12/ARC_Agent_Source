using ARC.Data.A1;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Odos;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Synthetic;

/// <summary>Synthetic / Assignment Evaluation Only — immutable generated corpus.</summary>
public sealed class SyntheticDataset
{
    public required int Seed { get; init; }
    public required DateOnly AsOf { get; init; }
    public required IReadOnlyList<SyntheticDealerProfile> Dealers { get; init; }
    public required IReadOnlyList<SyntheticOdosOpeningRow> OpeningHistory { get; init; }
    public required IReadOnlyList<LedgerAdjustmentFact> Adjustments { get; init; }
    public required IReadOnlyList<SyntheticRecoveryHeader> RecoveryHeaders { get; init; }
    public required IReadOnlyList<SecurityCheque> Cheques { get; init; }
    public required IReadOnlyList<ChequeReturnMemo> ReturnMemos { get; init; }
    public required IReadOnlyList<Dispute> Disputes { get; init; }
    public required IReadOnlyList<SyntheticPaymentHistoryFact> PaymentHistory { get; init; }
    public required IReadOnlyList<SyntheticPtpFact> Ptps { get; init; }
    public required IReadOnlyList<SyntheticFieldVisitFact> FieldVisits { get; init; }
    public required IReadOnlyList<SyntheticEvidenceFact> Evidence { get; init; }
    public required IReadOnlyList<SyntheticDemandNoticeFact> DemandNotices { get; init; }
    public required IReadOnlyList<SyntheticSection138Fact> Section138Facts { get; init; }
    public required IReadOnlyList<SyntheticMotherAccountLink> MotherAccountLinks { get; init; }
    public required IReadOnlyList<DealerSourceMapping> IdentityMappings { get; init; }
    public required SyntheticR6LineageCase R6LineageCase { get; init; }
    public required string DataClassification { get; init; }

    public IReadOnlyDictionary<SyntheticScenarioTag, SyntheticDealerProfile> ScenarioDealers { get; init; }
        = new Dictionary<SyntheticScenarioTag, SyntheticDealerProfile>();

    public int HistoryMonthCount =>
        OpeningHistory.Select(o => o.Snapshot.PeriodKey).Distinct(StringComparer.Ordinal).Count();
}

public sealed class SyntheticDatasetOptions
{
    public int Seed { get; init; } = 112_026_11;
    public int DealerCount { get; init; } = 2_500;
    public int HistoryMonths { get; init; } = 12;
    public DateOnly AsOf { get; init; } = new(2026, 3, 1);
    public string Company { get; init; } = "BPIL";

    /// <summary>Synthetic / Assignment Evaluation Only — mirrors fn_GetDefaultBusinessLimit binding, not hard-coded 5000.</summary>
    public decimal DefaultBusinessLimit { get; init; } = BusinessLineLimitOptions.SyntheticAssignmentDefaultLimit;
}

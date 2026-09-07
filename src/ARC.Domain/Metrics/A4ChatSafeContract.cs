using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Limitation;
using ARC.Domain.Rules;
using ARC.Domain.Workflow;

namespace ARC.Domain.Metrics;

/// <summary>Explicit availability of one A4 fact. No completeness percentage.</summary>
public enum A4FactAvailability
{
    Available = 0,
    Unavailable = 1,
    Tbc = 2,
    SyntheticOnly = 3,
    UnverifiedProduction = 4
}

public sealed record A4FactStatus(string Fact, A4FactAvailability Status, string? Detail = null);

public sealed record A4TbcIndicator(string Id, string Status, string Detail);

public sealed record A4AlertFact(string Kind, int DaysRemaining, DateOnly Deadline);

public sealed record A4LegalProvenance(
    bool Complete,
    string Status,
    string Source,
    string Detail);

/// <summary>
/// Chat/API-safe A4 view. Eligibility, cheque selection, memo codes and clock dates are
/// copied from deterministic A4/tool/workflow facts. TBC, completeness, provenance and
/// production blockers are server-generated and cannot be supplied by a caller or LLM.
/// </summary>
public sealed record A4ChatSafeContract(
    string DealerUrn,
    bool? Eligible,
    string? BlockReason,
    string? SelectedChequeNumber,
    string? SelectedChequeStatus,
    string? MemoReasonCode,
    DateOnly? MemoIssueDate,
    DateOnly? MemoReceivedDate,
    DateOnly? NoticeByDate,
    DateOnly? CureEndsDate,
    DateOnly? FileByDate,
    int? DaysRemaining,
    string? ClockStatus,
    IReadOnlyList<A4AlertFact> DueAlerts,
    string DeterministicExplanation,
    A4LegalProvenance Provenance,
    IReadOnlyList<A4FactStatus> DataCompleteness,
    IReadOnlyList<A4TbcIndicator> TbcIndicators,
    bool ProductionLegalBlockedOnApprovedStoredProcedure,
    IReadOnlyList<string> ProductionStoredProcedureBlockers);

/// <summary>Observed A4 facts only. No TBC, completeness, provenance, or statutory windows.</summary>
public sealed record A4ChatSafeFacts(
    string DealerUrn,
    bool? Eligible = null,
    string? BlockReason = null,
    string? SelectedChequeNumber = null,
    string? SelectedChequeStatus = null,
    string? MemoReasonCode = null,
    DateOnly? MemoIssueDate = null,
    DateOnly? MemoReceivedDate = null,
    DateOnly? NoticeByDate = null,
    DateOnly? CureEndsDate = null,
    DateOnly? FileByDate = null,
    int? DaysRemaining = null,
    string? ClockStatus = null,
    IReadOnlyList<A4AlertFact>? DueAlerts = null,
    bool AuthoritativeExposurePresent = false,
    bool DemandNoticePresent = false,
    bool EligibilityEvaluated = false);

/// <summary>Builds the A4 chat-safe contract. Completeness and TBC catalogs are central.</summary>
public static class A4ChatSafeAssembler
{
    public static readonly string[] ProductionStoredProcedureBlockers =
    [
        "ChequeRepository.ListChequesAsync",
        "ChequeRepository.ListReturnMemosAsync",
        "LedgerRepository.ListByDealerAsync",
        "DemandNoticeSourceOfRecord"
    ];

    public static IReadOnlyList<A4TbcIndicator> CentralTbcCatalog()
        =>
        [
            new("NoticeWindow", "Tbc",
                "Notice-window length is illustrative configuration, not Legal-confirmed. No NI Act period is declared here."),
            new("CureWindow", "Tbc",
                "Cure-window length is illustrative configuration, not Legal-confirmed."),
            new("FilingWindow", "Tbc",
                "Filing-window length is illustrative configuration, not Legal-confirmed."),
            new("ClockAnchor", "Tbc",
                "Whether the clock anchors on memo received date or memo issue date is not confirmed."),
            new("PresentationDateDefinition", "Tbc",
                "Presentation date is not a stored field. R2 currently uses memo received date. That meaning is not redefined here."),
            new("QualifyingReturnReasonMapping", "Tbc",
                "Bank/ODOS return codes versus the host qualifying set are not confirmed. The set is not changed here."),
            new("PaymentAfterDemandNotice", "Tbc",
                "Payment after demand notice is not an R2 predicate. No payment-after-notice rule is added here."),
            new("EnforceableDebtDefinition", "Tbc",
                "R2 uses A1 net recoverable exposure when present. ODOS outstanding and business-line limit are not bound to R2."),
            new("AlertOwner", "Tbc",
                "T-10/T-5/T-2 alerts have no owner on the clock type. Escalation owner is not defined here."),
            new("AlertTimingDefinition", "Tbc",
                "Alerts fire when remaining days exactly equal 10, 5 or 2. Whether that matches a 3-day filing warning is not confirmed."),
            new("DemandNoticeSourceOfRecord", "Tbc",
                "There is no production demand-notice repository. IssuedOn and ServedOn are never fabricated."),
            new("ReadyS138Mapping", "Tbc",
                "READY_S138 / HO BOUNCED are not mapped. No legal-status translation is added."),
            new("ChequeNewestSortKey", "Tbc",
                "ChequeSelection uses DepositDate for newest. Whether that is the assignment sort key is not confirmed."),
            new("R6Section138Applicability", "Tbc",
                "R6 lineage is notice-eligibility, not DecideSection138. Whether it must apply to S138 amounts is not confirmed."),
            new("A2ToSection138WorkflowRouting", "Tbc",
                "A2 Section138 tier does not start Workflow B. That routing is not changed here.")
        ];

    public static A4ChatSafeContract Assemble(A4ChatSafeFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (string.IsNullOrWhiteSpace(facts.DealerUrn))
            throw new ArgumentException("DealerUrn is required.", nameof(facts));

        var tbc = CentralTbcCatalog();
        var completeness = BuildCompleteness(facts);
        var provenance = new A4LegalProvenance(
            facts.EligibilityEvaluated && facts.AuthoritativeExposurePresent,
            facts.EligibilityEvaluated && facts.AuthoritativeExposurePresent ? "Complete" : "Incomplete",
            facts.AuthoritativeExposurePresent ? "CheckSection138Eligibility" : "ComputeNetExposure",
            facts.AuthoritativeExposurePresent
                ? "Eligibility, cheque, memo and clock dates are copied from deterministic A4 tools. Statutory windows are not Legal-confirmed."
                : "Authoritative A1 exposure was not available. Eligibility was not evaluated. No legal dates were calculated.");

        return new A4ChatSafeContract(
            facts.DealerUrn,
            facts.Eligible,
            NullIfEmpty(facts.BlockReason),
            NullIfEmpty(facts.SelectedChequeNumber),
            NullIfEmpty(facts.SelectedChequeStatus),
            NullIfEmpty(facts.MemoReasonCode),
            facts.MemoIssueDate,
            facts.MemoReceivedDate,
            facts.NoticeByDate,
            facts.CureEndsDate,
            facts.FileByDate,
            facts.DaysRemaining,
            NullIfEmpty(facts.ClockStatus),
            facts.DueAlerts ?? [],
            BuildExplanation(),
            provenance,
            completeness,
            tbc,
            ProductionLegalBlockedOnApprovedStoredProcedure: true,
            ProductionStoredProcedureBlockers);
    }

    public static A4ChatSafeContract FailClosed(string dealerUrn, string blockReason)
        => Assemble(new A4ChatSafeFacts(
            dealerUrn,
            Eligible: null,
            BlockReason: blockReason,
            AuthoritativeExposurePresent: false,
            EligibilityEvaluated: false));

    public static A4ChatSafeContract FromEligibility(
        string dealerUrn,
        EligibilityVerdict eligibility,
        LimitationClock? clock,
        IReadOnlyList<ClockAlert> alerts,
        SecurityCheque? cheque,
        ChequeReturnMemo? memo,
        DemandNotice? demandNotice,
        bool authoritativeExposurePresent)
        => Assemble(new A4ChatSafeFacts(
            dealerUrn,
            eligibility.Eligible,
            DeterministicBlockReason(eligibility),
            cheque?.ChequeNumber,
            cheque?.Status.ToString(),
            memo?.ReturnReasonCode,
            memo?.MemoIssueDate,
            memo?.MemoReceivedDate,
            clock?.NoticeByDate,
            clock?.CureWindowEnds,
            clock?.FileByDate,
            clock?.DaysRemaining,
            clock?.Status.ToString(),
            (alerts ?? []).Select(a => new A4AlertFact(a.Kind.ToString(), a.DaysRemaining, a.Deadline)).ToList(),
            authoritativeExposurePresent,
            demandNotice is not null,
            EligibilityEvaluated: true));

    public static A4ChatSafeContract FromClock(
        string dealerUrn,
        LimitationClock clock,
        IReadOnlyList<ClockAlert> alerts,
        SecurityCheque? cheque,
        ChequeReturnMemo memo,
        DemandNotice? demandNotice)
        => Assemble(new A4ChatSafeFacts(
            dealerUrn,
            Eligible: null,
            BlockReason: null,
            cheque?.ChequeNumber,
            cheque?.Status.ToString(),
            memo.ReturnReasonCode,
            memo.MemoIssueDate,
            memo.MemoReceivedDate,
            clock.NoticeByDate,
            clock.CureWindowEnds,
            clock.FileByDate,
            clock.DaysRemaining,
            clock.Status.ToString(),
            alerts.Select(a => new A4AlertFact(a.Kind.ToString(), a.DaysRemaining, a.Deadline)).ToList(),
            AuthoritativeExposurePresent: false,
            demandNotice is not null,
            EligibilityEvaluated: false));

    /// <summary>Copy persisted A4 facts from RecoveryState. Does not recompute dates or re-run R2.</summary>
    public static A4ChatSafeContract FromRecoveryState(RecoveryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Assemble(new A4ChatSafeFacts(
            state.DealerUrn.Value,
            state.Eligibility?.Eligible,
            DeterministicBlockReason(state.Eligibility),
            state.SelectedChequeNumber,
            state.SelectedChequeStatus,
            state.MemoReasonCode,
            state.MemoIssueDate,
            state.MemoReceivedDate,
            state.Clock?.NoticeByDate,
            state.Clock?.CureWindowEnds,
            state.Clock?.FileByDate,
            state.Clock?.DaysRemaining,
            state.Clock?.Status.ToString(),
            [],
            AuthoritativeExposurePresent: state.Exposure is not null,
            DemandNoticePresent: state.Clock?.NoticeServedDate is not null,
            EligibilityEvaluated: state.Eligibility is not null));
    }

    public static string? DeterministicBlockReason(EligibilityVerdict? eligibility)
    {
        if (eligibility is null || eligibility.Eligible)
            return null;
        if (!string.IsNullOrWhiteSpace(eligibility.BlockReason))
            return eligibility.BlockReason;
        return eligibility.RuleResults.FirstOrDefault(r => r.Blocks)?.Message;
    }

    private static IReadOnlyList<A4FactStatus> BuildCompleteness(A4ChatSafeFacts facts)
        =>
        [
            new("Eligibility", facts.EligibilityEvaluated ? A4FactAvailability.Available : A4FactAvailability.Unavailable,
                facts.EligibilityEvaluated
                    ? "Eligible is copied from DecideSection138. It is not LLM output."
                    : "Eligibility was not evaluated because authoritative A1 exposure was unavailable."),
            new("AuthoritativeExposure", facts.AuthoritativeExposurePresent ? A4FactAvailability.UnverifiedProduction : A4FactAvailability.Unavailable,
                "A1 ComputeNetExposure is the only amount source. Caller/LLM amounts are not accepted. Production ledger SP is not approved."),
            new("SelectedCheque", string.IsNullOrWhiteSpace(facts.SelectedChequeNumber) ? A4FactAvailability.Unavailable : A4FactAvailability.Available,
                "Cheque number/status are copied from ChequeSelection. Not generated."),
            new("ReturnMemo", string.IsNullOrWhiteSpace(facts.MemoReasonCode) ? A4FactAvailability.Unavailable : A4FactAvailability.Available,
                "Memo reason and dates are copied from ChequeReturnMemo. Mapping to bank codes remains TBC."),
            new("DemandNotice", facts.DemandNoticePresent ? A4FactAvailability.Available : A4FactAvailability.Unavailable,
                "No demand-notice repository. Missing IssuedOn/ServedOn remain null."),
            new("NoticeByDate", facts.NoticeByDate is null ? A4FactAvailability.Unavailable : A4FactAvailability.UnverifiedProduction,
                "Copied from LimitationClock when present. Window length is not Legal-confirmed."),
            new("CureEndsDate", facts.CureEndsDate is null ? A4FactAvailability.Unavailable : A4FactAvailability.UnverifiedProduction,
                "Copied when a served demand notice was present at clock compute time. Not fabricated."),
            new("FileByDate", facts.FileByDate is null ? A4FactAvailability.Unavailable : A4FactAvailability.UnverifiedProduction,
                "Copied when cure/file-by were already computed. Not fabricated."),
            new("LimitationDaysRemaining", facts.DaysRemaining is null ? A4FactAvailability.Unavailable : A4FactAvailability.UnverifiedProduction,
                "Copied from LimitationClock.DaysRemaining. Formula windows remain TBC."),
            new("DueAlerts", (facts.DueAlerts?.Count ?? 0) > 0 ? A4FactAvailability.Available : A4FactAvailability.Unavailable,
                "Copied from DueAlerts. Owner and 3-day versus T-2 timing remain TBC.")
        ];

    private static string BuildExplanation()
        =>
        "A4 copies deterministic eligibility, selected cheque, return-memo code and stored clock dates. " +
        "Notice, cure and filing windows are illustrative configuration and are not Legal-confirmed. " +
        "ODOS outstanding and business-line limit are not used as R2 inputs. " +
        "Missing demand-notice and presentation fields remain null. Production SQL reads remain blocked on approved stored-procedure contracts.";

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

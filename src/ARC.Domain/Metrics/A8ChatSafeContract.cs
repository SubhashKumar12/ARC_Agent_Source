using ARC.Domain.Entities;

namespace ARC.Domain.Metrics;

/// <summary>Explicit availability of one A8 fact. No completeness percentage.</summary>
public enum A8FactAvailability
{
    Available = 0,
    Unavailable = 1,
    Tbc = 2,
    SyntheticOnly = 3,
    UnverifiedProduction = 4
}

public sealed record A8FactStatus(string Fact, A8FactAvailability Status, string? Detail = null);

public sealed record A8TbcIndicator(string Id, string Status, string Detail);

public sealed record A8GateMetadata(
    string? Gate,
    string? ActorUpn,
    string? ActorRole,
    string? Decision,
    string? Reason,
    string? RecommendedAction,
    DateTimeOffset? DecidedUtc,
    string? CorrelationId,
    bool? WasOverride);

public sealed record A8ExceptionFact(
    string CycleId,
    string DealerUrn,
    string ExceptionKind,
    string? ExceptionDetail,
    string? WorkflowStatus,
    string? WaitingGate,
    string? Region,
    string? Depot,
    IReadOnlyList<A8GateMetadata> Gates);

public sealed record A8DealerFact(
    string DealerUrn,
    string? WorkflowStatus,
    string? WaitingGate,
    string? Region,
    string? Depot,
    IReadOnlyList<A8GateMetadata> Gates);

public sealed record A8SupervisionProvenance(
    bool Complete,
    string Status,
    string Source,
    string Detail);

/// <summary>
/// Chat/API-safe A8 supervision view. Exception kinds and statuses are copied from the
/// deterministic tool. TBC, completeness, provenance and production SP blockers are
/// server-generated and cannot be supplied by a caller or by LLM narration.
/// </summary>
public sealed record A8ChatSafeContract(
    string CycleId,
    string? Region,
    string? Depot,
    IReadOnlyList<A8ExceptionFact> Exceptions,
    IReadOnlyList<A8DealerFact> Dealers,
    string DeterministicExplanation,
    A8SupervisionProvenance Provenance,
    IReadOnlyList<A8FactStatus> DataCompleteness,
    IReadOnlyList<A8TbcIndicator> TbcIndicators,
    bool ProductionSupervisionBlockedOnApprovedStoredProcedure,
    IReadOnlyList<string> ProductionStoredProcedureBlockers);

public sealed record A8DealerSnapshot(
    string DealerUrn,
    string? WorkflowStatus,
    string? WaitingGate,
    string? Region,
    string? Depot,
    IReadOnlyList<A8GateMetadata> Gates);

public sealed record A8ExceptionSnapshot(
    string DealerUrn,
    string ExceptionKind,
    string? ExceptionDetail);

/// <summary>Facts the assembler may observe. It never accepts TBC, completeness, or provenance from a caller.</summary>
public sealed record A8ChatSafeFacts(
    string CycleId,
    string? Region,
    string? Depot,
    IReadOnlyList<A8ExceptionSnapshot> Exceptions,
    IReadOnlyList<A8DealerSnapshot> Dealers,
    bool BrokenPtpSuppliedByCaller = false);

/// <summary>
/// Builds the A8 chat-safe contract. Completeness and TBC catalogs are central and deterministic.
/// </summary>
public static class A8ChatSafeAssembler
{
    public static readonly string[] ProductionStoredProcedureBlockers =
    [
        "DealerRepository.ListByRegionAsync",
        "RecoveryCaseRepository.GetAsync",
        "RecoveryCaseRepository.ListRankedWorklistAsync",
        "GateDecisionRepository.ListAsync"
    ];

    public static A8GateMetadata FromGate(GateDecision gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        return new A8GateMetadata(
            gate.Gate.ToString(),
            NullIfEmpty(gate.ActorUpn),
            gate.ActorRole.ToString(),
            gate.Decision.ToString(),
            NullIfEmpty(gate.Reason),
            NullIfEmpty(gate.RecommendedAction),
            gate.DecidedUtc,
            NullIfEmpty(gate.CorrelationId.Value),
            gate.WasOverride);
    }

    public static A8ChatSafeContract Assemble(A8ChatSafeFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (string.IsNullOrWhiteSpace(facts.CycleId))
            throw new ArgumentException("CycleId is required.", nameof(facts));

        var dealers = (facts.Dealers ?? []).Select(ToDealerFact).ToList();
        var byDealer = dealers.ToDictionary(d => d.DealerUrn, StringComparer.OrdinalIgnoreCase);
        var exceptions = (facts.Exceptions ?? []).Select(e => ToExceptionFact(facts.CycleId, e, byDealer)).ToList();
        var tbc = CentralTbcCatalog();
        var completeness = BuildCompleteness(facts, dealers, exceptions);
        var provenance = new A8SupervisionProvenance(
            dealers.Count > 0 || exceptions.Count > 0,
            dealers.Count > 0 || exceptions.Count > 0 ? "Complete" : "Incomplete",
            "GetSupervisoryInsights",
            "Exception kinds, workflow status, waiting gate and gate metadata are copied from deterministic stores. Missing fields are left null. No effectiveness or learning metrics are computed.");

        return new A8ChatSafeContract(
            facts.CycleId,
            NullIfEmpty(facts.Region),
            NullIfEmpty(facts.Depot),
            exceptions,
            dealers,
            BuildExplanation(),
            provenance,
            completeness,
            tbc,
            ProductionSupervisionBlockedOnApprovedStoredProcedure: true,
            ProductionStoredProcedureBlockers);
    }

    public static IReadOnlyList<A8TbcIndicator> CentralTbcCatalog()
        =>
        [
            new("AsmRoleDefinition", "Tbc",
                "No ActorRole.Asm exists. ASM exception visibility is not defined."),
            new("HoRoleDefinition", "Tbc",
                "No ActorRole.Ho exists. HO exception visibility is not defined."),
            new("AsmHoCoverage", "Tbc",
                "Which dealers, depots or regions constitute an ASM/HO book is not confirmed."),
            new("LeverEffectivenessFormula", "Tbc",
                "Visit, notice, PTP, reconciliation, Section138 and override effectiveness formulas are not confirmed. No rates are computed."),
            new("LearningLoopRequirement", "Tbc",
                "Whether SP9 must persist action/outcome learning samples is not confirmed. No learning store is implemented."),
            new("BrokenPtpDefinition", "Tbc",
                "Broken PTP is not redefined here. Assignment meaning (payment vs chase vs commitment date) remains TBC."),
            new("OverdueGateDefinition", "Tbc",
                "Whether overdue means GateDecisionStatus.Expired only, or also waiting beyond an SLA, is not confirmed. No SLA is invented."),
            new("HoldReconcileSupervision", "Tbc",
                "Hold/Reconcile is not an A8 exception kind. Whether it belongs on the supervisory queue is not confirmed."),
            new("OverrideEffectivenessFormula", "Tbc",
                "GateDecision.WasOverride may be copied when present. Override frequency and outcome formulas are not confirmed.")
        ];

    private static A8DealerFact ToDealerFact(A8DealerSnapshot dealer)
        => new(
            dealer.DealerUrn,
            NullIfEmpty(dealer.WorkflowStatus),
            NullIfEmpty(dealer.WaitingGate),
            NullIfEmpty(dealer.Region),
            NullIfEmpty(dealer.Depot),
            dealer.Gates ?? []);

    private static A8ExceptionFact ToExceptionFact(
        string cycleId,
        A8ExceptionSnapshot exception,
        IReadOnlyDictionary<string, A8DealerFact> dealers)
    {
        dealers.TryGetValue(exception.DealerUrn, out var dealer);
        return new A8ExceptionFact(
            cycleId,
            exception.DealerUrn,
            exception.ExceptionKind,
            NullIfEmpty(exception.ExceptionDetail),
            dealer?.WorkflowStatus,
            dealer?.WaitingGate,
            dealer?.Region,
            dealer?.Depot,
            dealer?.Gates ?? []);
    }

    private static IReadOnlyList<A8FactStatus> BuildCompleteness(
        A8ChatSafeFacts facts,
        IReadOnlyList<A8DealerFact> dealers,
        IReadOnlyList<A8ExceptionFact> exceptions)
        =>
        [
            new("ExceptionQueue", A8FactAvailability.Available,
                "Exception kinds are copied from GetSupervisoryInsights. No KPIs are derived."),
            new("WorkflowStatus", dealers.Any(d => !string.IsNullOrWhiteSpace(d.WorkflowStatus))
                    ? A8FactAvailability.Available
                    : A8FactAvailability.Unavailable,
                "Workflow status is copied from recovery index or cycle state when present."),
            new("WaitingGate", dealers.Any(d => !string.IsNullOrWhiteSpace(d.WaitingGate))
                    || exceptions.Any(e => string.Equals(e.ExceptionKind, "WaitingForHuman", StringComparison.Ordinal))
                    ? A8FactAvailability.Available
                    : A8FactAvailability.Unavailable,
                "Waiting gate is copied from index/state when present. No SLA is invented."),
            new("GateMetadata", dealers.Any(d => d.Gates.Count > 0)
                    ? A8FactAvailability.Available
                    : A8FactAvailability.Unavailable,
                "Gate rows are copied from IGateDecisionRepository. Missing fields remain null."),
            new("BrokenPtp", facts.BrokenPtpSuppliedByCaller ? A8FactAvailability.UnverifiedProduction : A8FactAvailability.Unavailable,
                "Broken PTP is not host-defined on the Insights API. Existing date-miss kind is not treated as effectiveness."),
            new("ChaseStatus", A8FactAvailability.Unavailable,
                "IPtpChaseStore is not queried by A8. Chase is not labelled effectiveness."),
            new("LeverEffectiveness", A8FactAvailability.Unavailable,
                "No lever-effectiveness metric is computed."),
            new("LearningStore", A8FactAvailability.Unavailable,
                "No learning/outcome store exists."),
            new("AsmHoRoles", A8FactAvailability.Tbc,
                "ASM and HO are not ActorRole values.")
        ];

    private static string BuildExplanation()
        =>
        "A8 supervision copies deterministic exception kinds, workflow status, waiting gate and stored gate metadata. " +
        "ASM/HO roles, lever effectiveness, learning, broken-PTP assignment meaning, overdue-gate SLA, Hold/Reconcile supervision and override-outcome formulas remain TBC. " +
        "No percentages or effectiveness metrics are calculated. Production SQL reads remain blocked on approved stored-procedure contracts.";

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

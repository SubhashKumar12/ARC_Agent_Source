using Microsoft.Extensions.DependencyInjection;
using ARC.Agents.A6FieldOrchestration;
using ARC.Agents.Context;
using ARC.Agents.Models;
using ARC.Agents.Workflows.Models;
using ARC.Cli.Fakes;
using ARC.Cli.Runtime;
using ARC.Data.Messaging;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Identity;
using ARC.Domain.Limitation;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;
using ARC.Tools.Evidence;
using ARC.Tools.Identity;

namespace ARC.Cli.Scenarios;

internal sealed record ScenarioCheck(bool Pass, string Detail);

internal sealed record ScenarioOutcome(string Id, bool Passed, IReadOnlyList<ScenarioCheck> Checks, string Summary);

internal sealed class ScenarioRunner
{
    private readonly InMemoryArcStore _store;
    private readonly InMemoryDealerIdentityMappingRepository _identity;
    private readonly ResolveDealerIdentityTool _resolver;
    private readonly MemoryJsonCheckpointStore _maf;
    private readonly CliOutboundRecorder _outbound;
    private readonly CliWorkflowDriver _driver;
    private readonly FieldOrchestrationAgent _field;
    private readonly ILimitationClockService _clocks;
    private readonly IServiceBusPublisher _bus;

    public ScenarioRunner(IServiceProvider services)
    {
        _store = services.GetRequiredService<InMemoryArcStore>();
        _identity = services.GetRequiredService<InMemoryDealerIdentityMappingRepository>();
        _resolver = services.GetRequiredService<ResolveDealerIdentityTool>();
        _maf = services.GetRequiredService<MemoryJsonCheckpointStore>();
        _outbound = services.GetRequiredService<CliOutboundRecorder>();
        _driver = services.GetRequiredService<CliWorkflowDriver>();
        _field = services.GetRequiredService<FieldOrchestrationAgent>();
        _clocks = services.GetRequiredService<ILimitationClockService>();
        _bus = services.GetRequiredService<IServiceBusPublisher>();
    }

    public async Task<ScenarioOutcome> RunAsync(string id, CancellationToken cancellationToken)
    {
        Reset();
        return id.ToUpperInvariant() switch
        {
            "S1" => await S1Async(cancellationToken),
            "S2" => await S2Async(cancellationToken),
            "S3" => await S3Async(cancellationToken),
            "S4" => await S4Async(cancellationToken),
            "S5" => await S5Async(cancellationToken),
            "S6" => await S6Async(cancellationToken),
            "S7" => await S7Async(cancellationToken),
            "S8" => await S8Async(cancellationToken),
            "S9" => await S9Async(cancellationToken),
            _ => new ScenarioOutcome(id, false, [new(false, $"Unknown scenario '{id}'. Use S1–S9 or all.")], "Unknown scenario.")
        };
    }

    private void Reset()
    {
        _store.Reset();
        _identity.Reset();
        _maf.Clear();
        _outbound.Clear();
    }

    private async Task<ScenarioOutcome> S1Async(CancellationToken cancellationToken)
    {
        const string urn = "dealer:s1";
        const string cycle = "2026-03-s1";
        _store.SeedDealer(ScenarioSeed.Dealer(urn));
        _store.SeedLedger(ScenarioSeed.Invoice(urn, 100_000m, "SAP-FI-AR", "INV-S1"));

        var result = await _driver.RunAsync(Odos(cycle, urn, ScenarioSeed.AsOfOdos), autoApproveGates: true, cancellationToken);
        var state = RequireState(result);
        var checks = new List<ScenarioCheck>
        {
            Check(state.NoticeVerdict?.Decision == NoticeDecision.Issue, $"Notice {state.NoticeVerdict?.Decision} (expected Issue)"),
            Check(state.Status == WorkflowStatus.Completed, $"Status {state.Status} (expected Completed)"),
            Check(result.LastMessage?.Visit is not null, "Visit task created after G2"),
            Check(_outbound.Events.Any(e => e.StartsWith("notice-suppressed", StringComparison.Ordinal)), "Shadow suppressed demand notice"),
            Check(_outbound.Events.Any(e => e.StartsWith("visit-suppressed", StringComparison.Ordinal)), "Shadow suppressed visit despatch"),
            Check(!result.HaltedForHuman, "No leftover HITL halt")
        };
        return Outcome("S1", "Clean overdue, no credits → Issue → G1/G2 → visit (Shadow).", checks);
    }

    private async Task<ScenarioOutcome> S2Async(CancellationToken cancellationToken)
    {
        const string urn = "dealer:s2";
        const string cycle = "2026-03-s2";
        _store.SeedDealer(ScenarioSeed.Dealer(urn));
        _store.SeedLedger(
            ScenarioSeed.Invoice(urn, 100_000m, "SAP-FI-AR", "INV-S2"),
            ScenarioSeed.CreditNote(urn, 77_000m, "SAP-FI-AR", "CN-S2"));

        var result = await _driver.RunAsync(Odos(cycle, urn, ScenarioSeed.AsOfOdos), autoApproveGates: true, cancellationToken);
        var state = RequireState(result);
        var ratio = state.Exposure?.UnappliedCreditRatio ?? -1m;
        var checks = new List<ScenarioCheck>
        {
            Check(ratio > 0.40m, $"Unapplied credit ratio {ratio:P0} (R1b threshold 40%)"),
            Check(state.NoticeVerdict?.Decision == NoticeDecision.Reconcile, $"Notice {state.NoticeVerdict?.Decision} (expected Reconcile)"),
            Check(state.Status is WorkflowStatus.Terminated or WorkflowStatus.Blocked, $"Status {state.Status} (Finance reconcile, no gate)"),
            Check(state.WaitingGate is null, "No HITL gate opened"),
            Check(!_outbound.Events.Any(e => e.StartsWith("notice-suppressed", StringComparison.Ordinal)), "No notice despatch attempted")
        };
        return Outcome("S2", "77% of gross AR is credit note → R1b Reconcile → Finance.", checks);
    }

    private async Task<ScenarioOutcome> S3Async(CancellationToken cancellationToken)
    {
        const string urn = "dealer:s3";
        const string cycle = "2026-01-s3";
        _store.SeedDealer(ScenarioSeed.Dealer(urn));
        _store.SeedLedger(ScenarioSeed.Invoice(urn, 100_000m, "SAP-FI-AR", "INV-S3"));
        _store.SeedCheque(ScenarioSeed.BouncedCheque(urn));
        _store.SeedMemo(ScenarioSeed.Memo(urn, "FUNDS_INSUFFICIENT"));
        ScenarioSeed.SeedEvidence(_store, urn);

        var result = await _driver.RunAsync(
            S138(cycle, urn, ScenarioSeed.AsOfS138Open, ScenarioSeed.Notice(urn, cycle), ScenarioSeed.Section138Evidence(urn)),
            autoApproveGates: true,
            cancellationToken);
        var state = RequireState(result);
        var legal = _store.PeekLegalCase(new DealerUrn(urn));
        var checks = new List<ScenarioCheck>
        {
            Check(state.Eligibility?.Eligible == true, $"Eligible {state.Eligibility?.Eligible} — {state.Eligibility?.BlockReason}"),
            Check(state.Status == WorkflowStatus.Completed, $"Status {state.Status} (expected Completed after G4)"),
            Check(legal is { CompletenessScore: 1m, Gaps.Count: 0 }, $"Case file completeness {legal?.CompletenessScore} gaps {legal?.Gaps.Count}"),
            Check(state.Approvals.Count >= 3, $"Approvals {state.Approvals.Count} (G3, G2, G4)"),
            Note("Process B 60-day trigger vs illustrative 15+30 filing window is TBC; this run stays inside the clock so G3–G4 can execute.")
        };
        return Outcome("S3", "Qualifying bounce → S138 case file (Shadow, court filing out of scope).", checks);
    }

    private async Task<ScenarioOutcome> S4Async(CancellationToken cancellationToken)
    {
        const string urn = "dealer:s4";
        const string cycle = "2026-01-s4";
        _store.SeedDealer(ScenarioSeed.Dealer(urn));
        _store.SeedLedger(ScenarioSeed.Invoice(urn, 100_000m, "SAP-FI-AR", "INV-S4"));
        _store.SeedCheque(ScenarioSeed.BouncedCheque(urn));
        _store.SeedMemo(ScenarioSeed.Memo(urn, "SIGNATURE_MISMATCH"));

        var result = await _driver.RunAsync(
            S138(cycle, urn, ScenarioSeed.AsOfS138Open, ScenarioSeed.Notice(urn, cycle), []),
            autoApproveGates: true,
            cancellationToken);
        var state = RequireState(result);
        var checks = new List<ScenarioCheck>
        {
            Check(state.Eligibility?.Eligible == false, "R2 ineligible for SIGNATURE_MISMATCH"),
            Check(state.Status is WorkflowStatus.Blocked or WorkflowStatus.Terminated, $"Status {state.Status}"),
            Check(state.WaitingGate is null, "No courier / G3 path"),
            Check(state.Approvals.Count == 0, "No legal gate approvals")
        };
        return Outcome("S4", "Non-qualifying bounce reason blocks before courier.", checks);
    }

    private async Task<ScenarioOutcome> S5Async(CancellationToken cancellationToken)
    {
        const string urn = "dealer:s5";
        const string cycle = "2026-01-s5";
        var tsi = "tsi.west@paintco.local";
        _store.SeedDealer(ScenarioSeed.Dealer(urn, tsi: tsi));
        _store.SeedLedger(ScenarioSeed.Invoice(urn, 100_000m, "SAP-FI-AR", "INV-S5"));
        _store.SeedCheque(ScenarioSeed.BouncedCheque(urn));
        _store.SeedMemo(ScenarioSeed.Memo(urn, "FUNDS_INSUFFICIENT"));

        var result = await _driver.RunAsync(
            S138(cycle, urn, ScenarioSeed.AsOfT2, ScenarioSeed.Notice(urn, cycle), []),
            autoApproveGates: false,
            cancellationToken);
        var state = RequireState(result);
        var alerts = state.Clock is { } clock ? _clocks.DueAlerts(clock, ScenarioSeed.AsOfT2) : [];
        if (alerts.Any(a => a.Kind == ClockAlertKind.T2))
        {
            await _bus.PublishAlertAsync(
                $"T-2|{tsi}|dealer={urn}|deadline={state.Clock?.FileByDate:yyyy-MM-dd}|remaining={state.Clock?.DaysRemaining}",
                $"{cycle}|{urn}|T2",
                cancellationToken);
        }

        var checks = new List<ScenarioCheck>
        {
            Check(state.Clock?.DaysRemaining == 2, $"Clock remaining {state.Clock?.DaysRemaining} (domain T-2 fires when remaining equals 2)"),
            Check(alerts.Any(a => a.Kind == ClockAlertKind.T2), "DueAlerts contains T-2"),
            Check(_store.BusMessages.Any(m => m.Queue == "alert" && m.Body.Contains(tsi, StringComparison.Ordinal)), $"T-2 alert queued to named owner {tsi}"),
            Note("BRD S5 text said 3 days left; alerts fire at remaining == 10/5/2. Clock seeded so remaining equals 2.")
        };
        return Outcome("S5", "T-2 limitation alert to named covering TSI.", checks);
    }

    private async Task<ScenarioOutcome> S6Async(CancellationToken cancellationToken)
    {
        const string urn = "dealer:s6";
        const string cycle = "2026-03-s6";
        _store.SeedDealer(ScenarioSeed.Dealer(urn, moratorium: true));
        _store.SeedLedger(ScenarioSeed.Invoice(urn, 100_000m, "SAP-FI-AR", "INV-S6"));

        var result = await _driver.RunAsync(Odos(cycle, urn, ScenarioSeed.AsOfOdos), autoApproveGates: true, cancellationToken);
        var state = RequireState(result);
        var checks = new List<ScenarioCheck>
        {
            Check(state.Status is WorkflowStatus.Blocked or WorkflowStatus.Terminated, $"Status {state.Status}"),
            Check(state.TerminationReason?.Contains("R5", StringComparison.OrdinalIgnoreCase) == true
                  || state.TerminationReason?.Contains("moratorium", StringComparison.OrdinalIgnoreCase) == true,
                $"Termination '{state.TerminationReason}'"),
            Check(state.NoticeVerdict is null, "No notice decision after R5 block at A1"),
            Check(state.WaitingGate is null, "No HITL gate")
        };
        return Outcome("S6", "Insolvency moratorium blocks all action (R5).", checks);
    }

    private async Task<ScenarioOutcome> S7Async(CancellationToken cancellationToken)
    {
        const string urn = "dealer:s7";
        _store.SeedDealer(ScenarioSeed.Dealer(urn));
        var result = await _field.RunAsync(
            new FieldOrchestrationAgentRequest(
                FieldAgentAction.CapturePromiseToPay,
                urn,
                RecoveryTier.Notice,
                CommitmentDate: new DateOnly(2026, 4, 15),
                Amount: 25_000m,
                SpeechConfidence: 0.72m,
                ExistingPromise: null,
                VoiceTranscript: null,
                new AgentContext(ScenarioSeed.AsOfOdos, "2026-03-s7", "corr-s7", urn)),
            cancellationToken);

        var ptp = result.Promise;
        var checks = new List<ScenarioCheck>
        {
            Check(ptp is not null, "PTP structured"),
            Check(ptp?.RequiresTsiConfirmation == true, "RequiresTsiConfirmation at 0.72 (confirm-below 0.80 is a CLI demo threshold, not a legal ASR floor)"),
            Check(ptp?.Promise.ConfirmedByTsi == false, "Tool never sets ConfirmedByTsi"),
            Check(ptp?.DiscardedLowConfidence == false, "Not discarded (discard floor left unset)")
        };
        return Outcome("S7", "Voice PTP at 0.72 confidence requires TSI confirmation.", checks);
    }

    private async Task<ScenarioOutcome> S8Async(CancellationToken cancellationToken)
    {
        const string urn = "dealer:s8";
        const string cycle = "2026-03-s8";
        _store.SeedDealer(ScenarioSeed.Dealer(urn));
        _store.SeedLedger(ScenarioSeed.Invoice(urn, 100_000m, "SAP-FI-AR", "INV-S8"));

        var halted = await _driver.RunAsync(Odos(cycle, urn, ScenarioSeed.AsOfOdos), autoApproveGates: false, cancellationToken);
        var first = RequireState(halted);
        if (string.IsNullOrWhiteSpace(halted.WaitingPort))
            return Outcome("S8", "Resume from in-memory checkpoint after G1 pause.",
                [new(false, $"Expected G1 halt, status {first.Status} waiting {first.WaitingGate}")]);
        var (role, upn) = CliWorkflowDriver.RoleForPort(halted.WaitingPort);

        var resumed = await _driver.ResumeAsync(
            new GateResumeRequest
            {
                CycleId = cycle,
                DealerUrn = urn,
                Kind = ArcWorkflowKind.Odos,
                ActorUpn = upn,
                ActorRole = role,
                Decision = GateDecisionStatus.Approved,
                Reason = "CLI Shadow resume after simulated 6-day pause — expiry is not approval."
            },
            autoApproveRemaining: true,
            cancellationToken);
        var second = RequireState(resumed);

        var checks = new List<ScenarioCheck>
        {
            Check(halted.HaltedForHuman && first.WaitingGate == GateId.DepotManager, $"First halt G1 ({first.WaitingGate})"),
            Check(second.Status == WorkflowStatus.Completed, $"After resume status {second.Status}"),
            Check(second.Approvals.Any(a => a.Gate == GateId.DepotManager && a.Decision == GateDecisionStatus.Approved), "G1 approved by Depot Manager"),
            Note("Six-day pause is narrative only. asOf is not advanced; gate expiry is a distinct decision and is not treated as approval.")
        };
        return Outcome("S8", "Resume from in-memory checkpoint after G1 pause.", checks);
    }

    private async Task<ScenarioOutcome> S9Async(CancellationToken cancellationToken)
    {
        const string canonical = "dealer:s9";
        const string cycle = "2026-03-s9";
        const string tradeName = "M/S Jai Bhavani Hardware";

        _store.SeedDealer(ScenarioSeed.Dealer(canonical, sap: null, portal: null));
        await _identity.SaveAliasAsync(
            new DealerUrn(canonical),
            DealerAliasKinds.TradeLegalName,
            tradeName,
            cancellationToken);

        var sapSource = new SourceDealerRecord(DealerSourceSystems.Sap, "SAP-4411", tradeLegalName: tradeName);
        var portalSource = new SourceDealerRecord(DealerSourceSystems.Portal, "PORTAL-8822", tradeLegalName: tradeName);

        var sapResolved = await _resolver.ResolveAsync(sapSource, cancellationToken);
        var portalResolved = await _resolver.ResolveAsync(portalSource, cancellationToken);
        var sameUrn = sapResolved.CanonicalUrn is { } sapUrn
                      && portalResolved.CanonicalUrn is { } portalUrn
                      && string.Equals(sapUrn.Value, portalUrn.Value, StringComparison.OrdinalIgnoreCase);
        var canProceed = DealerIdentityProgression.AllowsExposure(sapResolved)
                         && DealerIdentityProgression.AllowsExposure(portalResolved)
                         && sameUrn;

        var checks = new List<ScenarioCheck>
        {
            Check(sapResolved.Status == DealerIdentityStatus.Resolved, $"SAP-4411 {sapResolved.Status} via {sapResolved.MatchKind}"),
            Check(portalResolved.Status == DealerIdentityStatus.Resolved, $"PORTAL-8822 {portalResolved.Status} via {portalResolved.MatchKind}"),
            Check(sameUrn, $"Both sources bound to {sapResolved.CanonicalUrn?.Value ?? "(none)"}"),
            Check(canProceed, "Resolved identity may proceed to exposure")
        };

        if (!canProceed)
        {
            checks.Add(Check(false, "Fail closed: ODOS was not started without a resolved canonical URN."));
            return Outcome("S9", "SAP + portal source records → ResolveDealerIdentityTool → one canonical URN.", checks);
        }

        var urn = sapResolved.CanonicalUrn?.Value
                  ?? throw new InvalidOperationException("S9 expected a resolved canonical URN.");
        _store.SeedLedger(
            ScenarioSeed.Invoice(urn, 60_000m, "SAP-FI-AR", "INV-SAP-4411"),
            ScenarioSeed.Invoice(urn, 40_000m, "DEALER-PORTAL", "INV-PORTAL-8822"));

        var result = await _driver.RunAsync(Odos(cycle, urn, ScenarioSeed.AsOfOdos), autoApproveGates: true, cancellationToken);
        var state = RequireState(result);
        var lines = await _store.ListByDealerAsync(new DealerUrn(urn), cancellationToken);
        var systems = lines.Select(l => l.Lineage.SourceSystem).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        checks.Add(Check(lines.Count == 2, $"Canonical URN holds {lines.Count} ledger lines after resolution"));
        checks.Add(Check(systems == 2, "SAP and portal source systems on the same dealer"));
        checks.Add(Check(state.Exposure?.GrossOpenAr.Amount == 100_000m, $"Gross AR {state.Exposure?.GrossOpenAr.Amount} (merged after resolution)"));
        checks.Add(Check(state.Status == WorkflowStatus.Completed, $"One ODOS case; status {state.Status}"));
        checks.Add(Note("S9 proves resolution → merge → exposure. Ledger is seeded only after ResolveDealerIdentityTool binds both sources."));
        return Outcome("S9", "SAP + portal source records → ResolveDealerIdentityTool → one canonical URN → merged exposure.", checks);
    }

    private static WorkflowRunRequest Odos(string cycle, string urn, DateOnly asOf) => new()
    {
        CycleId = cycle,
        DealerUrn = urn,
        AsOf = asOf,
        CorrelationId = $"corr-{cycle}",
        Mode = RunMode.Shadow,
        Kind = ArcWorkflowKind.Odos
    };

    private static WorkflowRunRequest S138(
        string cycle,
        string urn,
        DateOnly asOf,
        DemandNotice notice,
        IReadOnlyList<EvidenceItem> evidence) => new()
    {
        CycleId = cycle,
        DealerUrn = urn,
        AsOf = asOf,
        CorrelationId = $"corr-{cycle}",
        Mode = RunMode.Shadow,
        Kind = ArcWorkflowKind.Section138,
        DemandNotice = notice,
        Evidence = evidence
    };

    private static RecoveryState RequireState(CliRunResult result)
        => result.State ?? throw new InvalidOperationException("Workflow returned no RecoveryState.");

    private static ScenarioCheck Check(bool pass, string detail) => new(pass, detail);

    private static ScenarioCheck Note(string detail) => new(true, detail);

    private static ScenarioOutcome Outcome(string id, string summary, List<ScenarioCheck> checks)
        => new(id, checks.All(c => c.Pass), checks, summary);
}

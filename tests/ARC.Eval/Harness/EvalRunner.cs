using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Limitation;
using ARC.Domain.Rules;
using ARC.Domain.ValueObjects;
using ARC.Eval.Golden;
using ARC.Eval.Metrics;
using ARC.Tools.Field;
using ARC.Tools.Models;

namespace ARC.Eval.Harness;

internal sealed class AcceptanceReport
{
    public required int CaseCount { get; init; }
    public required int NoticeCases { get; init; }
    public required int NoticeMismatches { get; init; }
    public required int Issued { get; init; }
    public required int WrongfulIssued { get; init; }
    public required int LineageGapsOnIssue { get; init; }
    public required int Section138Cases { get; init; }
    public required int Section138Mismatches { get; init; }
    public required ClassificationMetrics Section138Classification { get; init; }
    public required CitationFaithfulnessEvaluation CitationFaithfulness { get; init; }
    public required DraftAmountEvaluation DraftAmount { get; init; }
    public required int ClockCases { get; init; }
    public required int ClockMisses { get; init; }
    public required int ClockFalseAlarms { get; init; }
    public required int VoicePtpCases { get; init; }
    public required int VoicePtpMismatches { get; init; }
    public required int VoicePtpConfirmedByTool { get; init; }
    public required int GovernanceFailures { get; init; }
    public required IReadOnlyList<string> Failures { get; init; }

    public decimal WrongfulNoticeRate => Issued == 0 ? 0m : (decimal)WrongfulIssued / Issued;

    public override string ToString()
    {
        var lines = new[]
        {
            $"ARC.Eval golden set: {CaseCount} labelled cases",
            $"  Notice: {NoticeCases}  mismatches={NoticeMismatches}  issued={Issued}  wrongful={WrongfulIssued}  rate={WrongfulNoticeRate:P2}  (target < 1%)",
            $"  R6 lineage gaps on Issue: {LineageGapsOnIssue}  (target 100% traceable)",
            $"  Section 138: {Section138Cases}  mismatches={Section138Mismatches}  {Section138Classification}",
            $"  Section 138 targets: precision≥0.95 recall≥0.98 — measured on LabelOracle grid (see Section138MetricsTests for coverage note)",
            $"  {CitationFaithfulness}",
            $"  {DraftAmount}",
            $"  Clock T-10/T-5/T-2: cases={ClockCases}  misses={ClockMisses}  falseAlarms={ClockFalseAlarms}  (target zero misses)",
            $"  Voice PTP: {VoicePtpCases}  mismatches={VoicePtpMismatches}  toolSetConfirmedByTsi={VoicePtpConfirmedByTool}  (must be 0)",
            $"  Governance R4/expiry: failures={GovernanceFailures}  (LLM authorization zero tolerance)",
            "  Retrieval faithfulness ≥0.95 and Document Intelligence floors are not scored (no labelled corpus)."
        };
        return string.Join(Environment.NewLine, lines);
    }
}

internal static class EvalRunner
{
    public static AcceptanceReport Run(IReadOnlyList<GoldenCase> cases)
        => Run(cases, RuleConfiguration.SourceIllustrative());

    public static AcceptanceReport Run(IReadOnlyList<GoldenCase> cases, RuleConfiguration configuration)
    {
        var engine = RuleEngine.CreateDefault(configuration);
        var clocks = new LimitationClockService();
        var failures = new List<string>();

        var noticeCases = 0;
        var noticeMismatch = 0;
        var issued = 0;
        var wrongful = 0;
        var lineageGaps = 0;
        var s138Cases = 0;
        var s138Mismatch = 0;
        var s138Tp = 0;
        var s138Fp = 0;
        var s138Tn = 0;
        var s138Fn = 0;
        var clockCases = 0;
        var clockMiss = 0;
        var clockFalse = 0;
        var ptpCases = 0;
        var ptpMismatch = 0;
        var ptpConfirmed = 0;
        var govFail = 0;

        foreach (var c in cases)
        {
            switch (c.Kind)
            {
                case GoldenKind.Notice:
                    noticeCases++;
                    ScoreNotice(c, engine, failures, ref noticeMismatch, ref issued, ref wrongful, ref lineageGaps);
                    break;
                case GoldenKind.Section138:
                    s138Cases++;
                    ScoreSection138(c, engine, failures, ref s138Mismatch, ref s138Tp, ref s138Fp, ref s138Tn, ref s138Fn);
                    break;
                case GoldenKind.ClockAlert:
                    clockCases++;
                    ScoreClock(c, clocks, failures, ref clockMiss, ref clockFalse);
                    break;
                case GoldenKind.VoicePtp:
                    ptpCases++;
                    ScoreVoicePtp(c, failures, ref ptpMismatch, ref ptpConfirmed);
                    break;
                case GoldenKind.Governance:
                    ScoreGovernance(c, failures, ref govFail);
                    break;
            }
        }

        return new AcceptanceReport
        {
            CaseCount = cases.Count,
            NoticeCases = noticeCases,
            NoticeMismatches = noticeMismatch,
            Issued = issued,
            WrongfulIssued = wrongful,
            LineageGapsOnIssue = lineageGaps,
            Section138Cases = s138Cases,
            Section138Mismatches = s138Mismatch,
            Section138Classification = new ClassificationMetrics(s138Tp, s138Fp, s138Tn, s138Fn),
            CitationFaithfulness = new NotConfiguredCitationFaithfulnessEvaluator().Evaluate(),
            DraftAmount = EvaluateDraftAmounts(cases, noticeCases),
            ClockCases = clockCases,
            ClockMisses = clockMiss,
            ClockFalseAlarms = clockFalse,
            VoicePtpCases = ptpCases,
            VoicePtpMismatches = ptpMismatch,
            VoicePtpConfirmedByTool = ptpConfirmed,
            GovernanceFailures = govFail,
            Failures = failures
        };
    }

    private static void ScoreNotice(
        GoldenCase c,
        RuleEngine engine,
        List<string> failures,
        ref int mismatch,
        ref int issued,
        ref int wrongful,
        ref int lineageGaps)
    {
        var actual = engine.DecideNotice(c.Context!);
        if (actual.Decision == NoticeDecision.Issue)
        {
            issued++;
            if (c.Context!.Exposure!.Lineage.Count == 0)
            {
                lineageGaps++;
                failures.Add($"{c.Id} Issue without R6 lineage.");
            }
        }

        if (actual.Decision != c.ExpectedNotice)
        {
            mismatch++;
            failures.Add($"{c.Id} notice expected {c.ExpectedNotice} actual {actual.Decision} ({c.Label})");
        }

        if (actual.Decision == NoticeDecision.Issue && c.ExpectedNotice != NoticeDecision.Issue)
        {
            wrongful++;
            failures.Add($"{c.Id} wrongful Issue ({c.Label})");
        }
    }

    private static void ScoreSection138(
        GoldenCase c,
        RuleEngine engine,
        List<string> failures,
        ref int mismatch,
        ref int tp,
        ref int fp,
        ref int tn,
        ref int fn)
    {
        var expected = c.ExpectedEligible ?? throw new InvalidOperationException("S138 case requires ExpectedEligible.");
        var actual = engine.DecideSection138(c.Context!).Eligible;

        switch (expected, actual)
        {
            case (true, true): tp++; break;
            case (true, false): fn++; break;
            case (false, true): fp++; break;
            case (false, false): tn++; break;
        }

        if (actual != expected)
        {
            mismatch++;
            failures.Add($"{c.Id} s138 expected eligible={expected} actual={actual} ({c.Label})");
        }
    }

    private static DraftAmountEvaluation EvaluateDraftAmounts(IReadOnlyList<GoldenCase> cases, int noticeCases)
    {
        _ = cases;
        return DraftAmountEvaluation.InsufficientLabels(noticeCases);
    }

    private static void ScoreClock(
        GoldenCase c,
        LimitationClockService clocks,
        List<string> failures,
        ref int miss,
        ref int falseAlarm)
    {
        var expected = c.ExpectedAlerts ?? [];
        var actual = clocks.DueAlerts(c.Context!.Clock!, c.Context.AsOf).Select(a => a.Kind).ToList();
        foreach (var kind in expected)
        {
            if (!actual.Contains(kind))
            {
                miss++;
                failures.Add($"{c.Id} clock miss {kind} ({c.Label})");
            }
        }

        foreach (var kind in actual)
        {
            if (!expected.Contains(kind))
            {
                falseAlarm++;
                failures.Add($"{c.Id} clock false alarm {kind} ({c.Label})");
            }
        }
    }

    private static void ScoreVoicePtp(
        GoldenCase c,
        List<string> failures,
        ref int mismatch,
        ref int confirmedByTool)
    {
        var discardBelow = c.Id == "P07" ? 0.50m : (decimal?)null;
        var tool = new FieldOrchestrationTool(
            new UnusedDealerRepository(),
            Options.Create(new ArcToolsOptions
            {
                VoicePtpConfirmBelow = 0.80m,
                VoicePtpDiscardBelow = discardBelow
            }),
            NullLogger<FieldOrchestrationTool>.Instance);

        var result = tool.CapturePromiseToPay(new CapturePromiseToPayRequest(
            "dealer:eval-ptp",
            new DateOnly(2026, 4, 15),
            25_000m,
            c.RequestConfirmedByTsi,
            c.SpeechConfidence,
            new DateOnly(2026, 3, 1),
            "eval-ptp",
            "corr-eval-ptp"));

        if (result.Promise.ConfirmedByTsi)
        {
            confirmedByTool++;
            failures.Add($"{c.Id} tool set ConfirmedByTsi — TSI must remain human.");
        }

        if (result.RequiresTsiConfirmation != c.ExpectedRequiresTsi
            || result.DiscardedLowConfidence != c.ExpectedDiscarded
            || result.Promise.ConfirmedByTsi != false)
        {
            mismatch++;
            failures.Add(
                $"{c.Id} PTP expected tsi={c.ExpectedRequiresTsi} discarded={c.ExpectedDiscarded} " +
                $"actual tsi={result.RequiresTsiConfirmation} discarded={result.DiscardedLowConfidence} confirmed={result.Promise.ConfirmedByTsi}");
        }
    }

    private static void ScoreGovernance(GoldenCase c, List<string> failures, ref int fail)
    {
        if (c.ExpectedAgentBlocked)
        {
            try
            {
                GateDecision.Create(
                    GateId.DepotManager,
                    "agent@system",
                    ActorRole.Agent,
                    GateDecisionStatus.Approved,
                    "model said yes",
                    CorrelationId.New());
                fail++;
                failures.Add($"{c.Id} Agent was allowed to approve (R4).");
            }
            catch (ARC.Domain.Exceptions.InvalidGateDecisionException)
            {
                // expected
            }

            return;
        }

        if (c.ExpectedExpiryNotApproval)
        {
            var expired = GateDecision.Expire(GateId.DepotManager, CorrelationId.New());
            if (expired.AllowsProgression || expired.Decision != GateDecisionStatus.Expired)
            {
                fail++;
                failures.Add($"{c.Id} expiry treated as approval.");
            }

            return;
        }

        var ok = GateDecision.Create(
            GateId.DepotManager,
            "depot.manager@paintco.local",
            ActorRole.DepotManager,
            GateDecisionStatus.Approved,
            "human G1",
            CorrelationId.New());
        if (!ok.AllowsProgression)
        {
            fail++;
            failures.Add($"{c.Id} DepotManager approval did not allow progression.");
        }
    }

    private sealed class UnusedDealerRepository : IDealerRepository
    {
        public Task<Dealer?> GetAsync(DealerUrn urn, CancellationToken cancellationToken)
            => Task.FromResult<Dealer?>(null);

        public Task<IReadOnlyList<Dealer>> ListByRegionAsync(string region, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Dealer>>([]);

        public Task<IReadOnlyList<Dealer>> ListAllAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Dealer>>([]);
    }
}

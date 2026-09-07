using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Exceptions;
using ARC.Domain.Limitation;
using ARC.Domain.Metrics;
using ARC.Domain.Rules;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;

namespace ARC.Domain.Tests;

public class MetricContractTests
{
    [Fact]
    public void Compute_subtracts_all_source_components()
    {
        var exposure = MetricContract.Compute(
            new DealerUrn("dealer-1"),
            new DateOnly(2026, 8, 1),
            new Money(100_000m),
            new Money(10_000m),
            new Money(5_000m),
            new Money(2_000m),
            new Money(3_000m),
            new Money(1_000m),
            [new LineItemRef("SAP-FI-AR", "BSEG", "1", 100_000m, new DateOnly(2026, 1, 1))],
            fullyReconciled: true);

        Assert.Equal(79_000m, exposure.NetRecoverableExposure.Amount);
        Assert.Equal(ReconciliationStatus.Reconciled, exposure.Status);
    }
}

public class NoticeRulesTests
{
    private static RuleContext Context(
        ExposureBreakdown exposure,
        Dealer? dealer = null,
        Dispute? dispute = null,
        PromiseToPay? ptp = null)
        => new()
        {
            Exposure = exposure,
            Dealer = dealer ?? new Dealer(exposure.DealerUrn, underInsolvencyMoratorium: false),
            OpenDispute = dispute,
            ActivePromiseToPay = ptp,
            Configuration = RuleConfiguration.SourceIllustrative(),
            AsOf = exposure.AsOf
        };

    private static ExposureBreakdown CleanExposure(decimal unapplied = 0m, bool reconciled = true)
        => MetricContract.Compute(
            new DealerUrn("dealer-1"),
            new DateOnly(2026, 8, 1),
            new Money(100_000m),
            new Money(unapplied),
            Money.Zero, Money.Zero, Money.Zero, Money.Zero,
            [new LineItemRef("SAP-FI-AR", "BSEG", "1", 100_000m, new DateOnly(2026, 1, 1))],
            fullyReconciled: reconciled);

    [Fact]
    public void R1a_blocks_unreconciled()
    {
        var engine = RuleEngine.CreateDefault(RuleConfiguration.SourceIllustrative());
        var verdict = engine.DecideNotice(Context(CleanExposure(reconciled: false)));
        Assert.Equal(NoticeDecision.Hold, verdict.Decision);
        Assert.Contains(verdict.RuleResults, r => r.RuleId == "R1a" && r.Blocks);
    }

    [Fact]
    public void R1b_blocks_when_credit_ratio_exceeds_40_percent()
    {
        var engine = RuleEngine.CreateDefault(RuleConfiguration.SourceIllustrative());
        var verdict = engine.DecideNotice(Context(CleanExposure(unapplied: 77_000m)));
        Assert.Equal(NoticeDecision.Reconcile, verdict.Decision);
        Assert.Contains(verdict.RuleResults, r => r.RuleId == "R1b" && r.Blocks);
    }

    [Fact]
    public void R1c_holds_when_dispute_under_review()
    {
        var engine = RuleEngine.CreateDefault(RuleConfiguration.SourceIllustrative());
        var dispute = new Dispute(new DealerUrn("dealer-1"), DisputeStatus.UnderReview, "DSP-1");
        var verdict = engine.DecideNotice(Context(CleanExposure(), dispute: dispute));
        Assert.Equal(NoticeDecision.Hold, verdict.Decision);
    }

    [Fact]
    public void R1c_does_not_hold_on_unconfirmed_ptp_candidate()
    {
        var engine = RuleEngine.CreateDefault(RuleConfiguration.SourceIllustrative());
        var ptp = new PromiseToPay(
            new DealerUrn("dealer-1"),
            new DateOnly(2026, 8, 15),
            new Money(25_000m),
            confirmedByTsi: false);
        var verdict = engine.DecideNotice(Context(CleanExposure(), ptp: ptp));
        Assert.Equal(NoticeDecision.Issue, verdict.Decision);
        Assert.DoesNotContain(verdict.RuleResults, r => r.RuleId == "R1c" && r.Blocks);
    }

    [Fact]
    public void R1c_holds_when_confirmed_ptp_is_within_grace()
    {
        var engine = RuleEngine.CreateDefault(RuleConfiguration.SourceIllustrative());
        var ptp = new PromiseToPay(
            new DealerUrn("dealer-1"),
            new DateOnly(2026, 8, 15),
            new Money(25_000m),
            confirmedByTsi: true);
        var verdict = engine.DecideNotice(Context(CleanExposure(), ptp: ptp));
        Assert.Equal(NoticeDecision.Hold, verdict.Decision);
        Assert.Contains(verdict.RuleResults, r => r.RuleId == "R1c" && r.Blocks);
    }

    [Fact]
    public void R5_blocks_moratorium()
    {
        var engine = RuleEngine.CreateDefault(RuleConfiguration.SourceIllustrative());
        var dealer = new Dealer(new DealerUrn("dealer-1"), underInsolvencyMoratorium: true);
        var verdict = engine.DecideNotice(Context(CleanExposure(), dealer: dealer));
        Assert.Equal(NoticeDecision.Hold, verdict.Decision);
        Assert.Contains(verdict.RuleResults, r => r.RuleId == "R5" && r.Blocks);
    }

    [Fact]
    public void R6_blocks_when_lineage_missing()
    {
        var exposure = MetricContract.Compute(
            new DealerUrn("dealer-1"),
            new DateOnly(2026, 8, 1),
            new Money(10_000m),
            Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero,
            [],
            fullyReconciled: true);
        var engine = RuleEngine.CreateDefault(RuleConfiguration.SourceIllustrative());
        var verdict = engine.DecideNotice(Context(exposure));
        Assert.Contains(verdict.RuleResults, r => r.RuleId == "R6" && r.Blocks);
    }

    [Fact]
    public void Clean_position_issues_notice()
    {
        var engine = RuleEngine.CreateDefault(RuleConfiguration.SourceIllustrative());
        var verdict = engine.DecideNotice(Context(CleanExposure()));
        Assert.Equal(NoticeDecision.Issue, verdict.Decision);
        Assert.True(verdict.RequiresDepotManagerGate);
    }

    [Fact]
    public void R1a_throws_when_exposure_missing()
    {
        var rule = new R1aUnreconciledRule("2026.03.1");
        var context = new RuleContext
        {
            Exposure = null,
            Dealer = new Dealer(new DealerUrn("x"), false),
            Configuration = RuleConfiguration.SourceIllustrative(),
            AsOf = new DateOnly(2026, 8, 1)
        };
        Assert.Throws<MissingRulePrerequisiteException>(() => rule.Evaluate(context));
    }
}

public class Section138RuleTests
{
    [Fact]
    public void R2_blocks_non_qualifying_return_code()
    {
        var urn = new DealerUrn("dealer-1");
        var exposure = MetricContract.Compute(
            urn, new DateOnly(2026, 8, 1), new Money(50_000m),
            Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero,
            [new LineItemRef("SAP-FI-AR", "BSEG", "1", 50_000m, new DateOnly(2026, 1, 1))],
            true);
        var cheque = new SecurityCheque(urn, "CHQ-1", new Money(50_000m), ChequeStatus.Bounced);
        var memo = new ChequeReturnMemo(urn, "CHQ-1", "SIGNATURE_MISMATCH", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2));
        var engine = RuleEngine.CreateDefault(RuleConfiguration.SourceIllustrative());
        var verdict = engine.DecideSection138(new RuleContext
        {
            Exposure = exposure,
            Dealer = new Dealer(urn, false),
            Cheque = cheque,
            ReturnMemo = memo,
            Configuration = RuleConfiguration.SourceIllustrative(),
            AsOf = new DateOnly(2026, 8, 1)
        });
        Assert.False(verdict.Eligible);
    }
}

public class LimitationClockTests
{
    [Fact]
    public void Notice_by_uses_configured_window_from_anchor()
    {
        var memo = new ChequeReturnMemo(
            new DealerUrn("d1"), "1", "FUNDS_INSUFFICIENT",
            memoIssueDate: new DateOnly(2026, 1, 1),
            memoReceivedDate: new DateOnly(2026, 1, 10));
        var config = RuleConfiguration.SourceIllustrative();
        var clock = new LimitationClockService().Compute(memo, notice: null, asOf: new DateOnly(2026, 1, 10), config);
        Assert.Equal(new DateOnly(2026, 2, 9), clock.NoticeByDate);
        Assert.Null(clock.CureWindowEnds);
        Assert.Equal(ClockStatus.Warning, clock.Status);
    }

    [Fact]
    public void Expired_when_asOf_after_file_by()
    {
        var urn = new DealerUrn("d1");
        var memo = new ChequeReturnMemo(urn, "1", "FUNDS_INSUFFICIENT", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1));
        var notice = new DemandNotice(urn, new CycleId("c1"), new DateOnly(2026, 1, 5), new Money(1), servedOn: new DateOnly(2026, 1, 5));
        var config = RuleConfiguration.SourceIllustrative();
        var clock = new LimitationClockService().Compute(memo, notice, asOf: new DateOnly(2026, 12, 31), config);
        Assert.Equal(ClockStatus.Expired, clock.Status);
        Assert.True(clock.BlocksProgression);
    }

    [Fact]
    public void T2_alert_when_exactly_two_days_remain()
    {
        var clock = new LimitationClock(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 1, 10),
            new DateOnly(2026, 1, 25),
            new DateOnly(2026, 2, 24),
            DaysRemaining: 2,
            ClockStatus.Critical);
        var alerts = new LimitationClockService().DueAlerts(clock, new DateOnly(2026, 2, 22));
        Assert.Contains(alerts, a => a.Kind == ClockAlertKind.T2);
    }
}

public class GateDecisionTests
{
    [Fact]
    public void Agent_cannot_approve_R4()
    {
        Assert.Throws<InvalidGateDecisionException>(() =>
            GateDecision.Create(
                GateId.DepotManager,
                "agent@system",
                ActorRole.Agent,
                GateDecisionStatus.Approved,
                "ok",
                CorrelationId.New()));
    }

    [Fact]
    public void Expiry_is_never_approval()
    {
        var decision = GateDecision.Expire(GateId.DepotManager, CorrelationId.New());
        Assert.Equal(GateDecisionStatus.Expired, decision.Decision);
        Assert.False(decision.AllowsProgression);
        Assert.Equal("gate_expired", decision.Reason);
    }
}

public class WorkflowStateTests
{
    [Fact]
    public void Checkpoint_idempotency_key_is_cycle_dealer_node()
    {
        var cp = new WorkflowCheckpoint(
            new CycleId("2026-08"),
            new DealerUrn("dealer-1"),
            "A3",
            WorkflowStatus.WaitingForHuman,
            DateTimeOffset.UtcNow);
        Assert.Equal("2026-08|dealer-1|A3", cp.IdempotencyKey);
    }
}

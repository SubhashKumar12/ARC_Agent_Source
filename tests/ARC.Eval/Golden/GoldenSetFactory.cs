using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Limitation;
using ARC.Domain.Metrics;
using ARC.Domain.Rules;
using ARC.Domain.ValueObjects;

namespace ARC.Eval.Golden;

/// <summary>
/// Synthetic labelled set. Labels come from <see cref="LabelOracle"/> (BRD priority), not from RuleEngine.
/// Architecture target is ~200 cases — this factory emits a full notice grid plus S138, clock, PTP, and governance.
/// </summary>
internal static class GoldenSetFactory
{
    private static readonly DateOnly AsOf = new(2026, 3, 1);
    private static readonly DateOnly MemoReceived = new(2026, 1, 1);
    private static readonly DateOnly ServedOn = new(2026, 1, 10);
    private static readonly RuleConfiguration Config = RuleConfiguration.SourceIllustrative();

    public static IReadOnlyList<GoldenCase> Create()
    {
        var cases = new List<GoldenCase>(280);
        cases.AddRange(NoticeGrid());
        cases.AddRange(Section138Grid());
        cases.AddRange(ClockGrid());
        cases.AddRange(VoicePtpGrid());
        cases.AddRange(GovernanceGrid());
        return cases;
    }

    private static IEnumerable<GoldenCase> NoticeGrid()
    {
        decimal[] credits = [0m, 39_000m, 40_000m, 40_010m, 77_000m];
        bool[] flags = [false, true];
        var n = 0;
        foreach (var credit in credits)
        foreach (var moratorium in flags)
        foreach (var unreconciled in flags)
        foreach (var dispute in flags)
        foreach (var ptp in flags)
        foreach (var lineage in flags)
        {
            n++;
            var urn = new DealerUrn($"eval-notice-{n:D3}");
            var context = NoticeContext(urn, credit, moratorium, unreconciled, dispute, ptp, lineage);
            yield return new GoldenCase(
                Id: $"N{n:D3}",
                Kind: GoldenKind.Notice,
                Label: $"credit={credit} moratorium={moratorium} unrec={unreconciled} dispute={dispute} ptp={ptp} lineage={lineage}",
                Context: context,
                ExpectedNotice: LabelOracle.Notice(context));
        }
    }

    private static IEnumerable<GoldenCase> Section138Grid()
    {
        string[] codes =
        [
            "FUNDS_INSUFFICIENT",
            "EXCEEDS_ARRANGEMENT",
            "ACCOUNT_CLOSED",
            "STOP_PAYMENT_QUALIFIED",
            "SIGNATURE_MISMATCH"
        ];
        int[] remainingChoices = [20, 10, 2, -1];
        bool[] flags = [false, true];
        var n = 0;
        foreach (var code in codes)
        foreach (var remaining in remainingChoices)
        foreach (var zeroNet in flags)
        foreach (var outsideValidity in flags)
        foreach (var moratorium in flags)
        {
            n++;
            var urn = new DealerUrn($"eval-s138-{n:D3}");
            var context = Section138Context(urn, code, remaining, zeroNet, outsideValidity, moratorium);
            yield return new GoldenCase(
                Id: $"L{n:D3}",
                Kind: GoldenKind.Section138,
                Label: $"code={code} remaining={remaining} zeroNet={zeroNet} outsideValidity={outsideValidity} moratorium={moratorium}",
                Context: context,
                ExpectedEligible: LabelOracle.Section138Eligible(context));
        }
    }

    private static IEnumerable<GoldenCase> ClockGrid()
    {
        var clocks = new LimitationClockService();
        var urn = new DealerUrn("eval-clock");
        var memo = new ChequeReturnMemo(urn, "CHQ-CLK", "FUNDS_INSUFFICIENT", MemoReceived, MemoReceived);
        var notice = new DemandNotice(urn, new CycleId("eval-clock"), ServedOn, new Money(1), servedOn: ServedOn);
        var fileBy = ServedOn.AddDays(Config.CureWindowDays + Config.FilingWindowDays);

        for (var remaining = -1; remaining <= 15; remaining++)
        {
            var asOf = fileBy.AddDays(-remaining);
            var clock = clocks.Compute(memo, notice, asOf, Config);
            yield return new GoldenCase(
                Id: $"C{remaining + 1:D2}",
                Kind: GoldenKind.ClockAlert,
                Label: $"remaining={remaining} asOf={asOf:yyyy-MM-dd} fileBy={fileBy:yyyy-MM-dd}",
                Context: new RuleContext
                {
                    Exposure = null,
                    Dealer = new Dealer(urn, false),
                    ReturnMemo = memo,
                    DemandNotice = notice,
                    Clock = clock,
                    Configuration = Config,
                    AsOf = asOf
                },
                ExpectedAlerts: LabelOracle.Alerts(clock, asOf));
        }
    }

    private static IEnumerable<GoldenCase> VoicePtpGrid()
    {
        var confirm = 0.80m;
        decimal? discard = null;
        (decimal? conf, bool requestConfirmed)[] rows =
        [
            (0.72m, false),
            (0.80m, false),
            (0.90m, false),
            (0.72m, true),
            (0.50m, false),
            (null, false)
        ];

        var i = 0;
        foreach (var (conf, confirmed) in rows)
        {
            i++;
            var (requires, discarded) = LabelOracle.VoicePtp(conf, confirmed, confirm, discard);
            yield return new GoldenCase(
                Id: $"P{i:D2}",
                Kind: GoldenKind.VoicePtp,
                Label: $"asr={conf} requestConfirmed={confirmed} confirmBelow={confirm}",
                ExpectedRequiresTsi: requires,
                ExpectedPtpConfirmedByTsi: false,
                SpeechConfidence: conf,
                RequestConfirmedByTsi: confirmed,
                ExpectedDiscarded: discarded);
        }

        var (reqDiscard, discDiscard) = LabelOracle.VoicePtp(0.40m, false, confirm, 0.50m);
        yield return new GoldenCase(
            Id: "P07",
            Kind: GoldenKind.VoicePtp,
            Label: "asr=0.40 discardBelow=0.50 (CLI does not set a legal discard floor; this row only checks the tool contract)",
            ExpectedRequiresTsi: reqDiscard,
            ExpectedPtpConfirmedByTsi: false,
            SpeechConfidence: 0.40m,
            ExpectedDiscarded: discDiscard);
    }

    private static IEnumerable<GoldenCase> GovernanceGrid()
    {
        yield return new GoldenCase(
            Id: "G01",
            Kind: GoldenKind.Governance,
            Label: "R4: ActorRole.Agent cannot approve",
            ExpectedAgentBlocked: true);
        yield return new GoldenCase(
            Id: "G02",
            Kind: GoldenKind.Governance,
            Label: "Gate expiry is never approval",
            ExpectedExpiryNotApproval: true);
        yield return new GoldenCase(
            Id: "G03",
            Kind: GoldenKind.Governance,
            Label: "DepotManager may approve G1",
            ExpectedAgentBlocked: false);
    }

    private static RuleContext NoticeContext(
        DealerUrn urn,
        decimal credit,
        bool moratorium,
        bool unreconciled,
        bool dispute,
        bool ptp,
        bool lineage)
    {
        IReadOnlyList<LineItemRef> rows = lineage
            ? [new LineItemRef("SAP-FI-AR", "BSEG", urn.Value, 100_000m, new DateOnly(2025, 11, 1))]
            : [];
        var exposure = MetricContract.Compute(
            urn,
            AsOf,
            new Money(100_000m),
            new Money(credit),
            Money.Zero, Money.Zero, Money.Zero, Money.Zero,
            rows,
            fullyReconciled: !unreconciled);

        return new RuleContext
        {
            Exposure = exposure,
            Dealer = new Dealer(urn, moratorium, sapCode: "SAP-EVAL"),
            OpenDispute = dispute ? new Dispute(urn, DisputeStatus.UnderReview, "DSP-EVAL") : null,
            ActivePromiseToPay = ptp
                ? new PromiseToPay(urn, new DateOnly(2026, 4, 15), new Money(10_000m), confirmedByTsi: true)
                : null,
            Configuration = Config,
            AsOf = AsOf
        };
    }

    private static RuleContext Section138Context(
        DealerUrn urn,
        string code,
        int remaining,
        bool zeroNet,
        bool outsideValidity,
        bool moratorium)
    {
        var credits = zeroNet ? 100_000m : 0m;
        var exposure = MetricContract.Compute(
            urn,
            AsOf,
            new Money(100_000m),
            new Money(credits),
            Money.Zero, Money.Zero, Money.Zero, Money.Zero,
            [new LineItemRef("SAP-FI-AR", "BSEG", urn.Value, 100_000m, new DateOnly(2025, 11, 1))],
            fullyReconciled: true);

        var validityEnd = outsideValidity ? MemoReceived.AddDays(-1) : new DateOnly(2027, 1, 1);
        var cheque = new SecurityCheque(
            urn, "CHQ-EVAL", new Money(100_000m), ChequeStatus.Bounced,
            depositDate: MemoReceived, validityEnd: validityEnd);
        var memo = new ChequeReturnMemo(urn, "CHQ-EVAL", code, MemoReceived, MemoReceived);
        var fileBy = ServedOn.AddDays(Config.CureWindowDays + Config.FilingWindowDays);
        var asOf = fileBy.AddDays(-remaining);
        var notice = new DemandNotice(urn, new CycleId("eval-s138"), ServedOn, new Money(100_000m), servedOn: ServedOn);
        var clock = new LimitationClockService().Compute(memo, notice, asOf, Config);

        return new RuleContext
        {
            Exposure = exposure,
            Dealer = new Dealer(urn, moratorium),
            Cheque = cheque,
            ReturnMemo = memo,
            DemandNotice = notice,
            Clock = clock,
            Configuration = Config,
            AsOf = asOf
        };
    }
}

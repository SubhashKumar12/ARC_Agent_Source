using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Limitation;
using ARC.Domain.Metrics;
using ARC.Domain.Rules;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;

namespace ARC.Domain.Tests;

public sealed class A4ChatSafeContractTests
{
    [Fact]
    public void Contract_copies_deterministic_eligibility_cheque_and_clock()
    {
        var urn = new DealerUrn("dealer:11m");
        var cheque = new SecurityCheque(urn, "CHQ-9001", new Money(100_000m), ChequeStatus.Bounced, validityEnd: new DateOnly(2027, 1, 1));
        var memo = new ChequeReturnMemo(urn, "CHQ-9001", "FUNDS_INSUFFICIENT", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1));
        var clock = new LimitationClock(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            null,
            null,
            null,
            20,
            ClockStatus.Warning);
        var eligibility = new EligibilityVerdict(true, []);

        var contract = A4ChatSafeAssembler.FromEligibility(
            urn.Value,
            eligibility,
            clock,
            [new ClockAlert(ClockAlertKind.T10, 10, new DateOnly(2026, 1, 31))],
            cheque,
            memo,
            demandNotice: null,
            authoritativeExposurePresent: true);

        Assert.True(contract.Eligible);
        Assert.Null(contract.BlockReason);
        Assert.Equal("CHQ-9001", contract.SelectedChequeNumber);
        Assert.Equal("Bounced", contract.SelectedChequeStatus);
        Assert.Equal("FUNDS_INSUFFICIENT", contract.MemoReasonCode);
        Assert.Equal(new DateOnly(2026, 1, 1), contract.MemoReceivedDate);
        Assert.Equal(new DateOnly(2026, 1, 31), contract.NoticeByDate);
        Assert.Null(contract.CureEndsDate);
        Assert.Null(contract.FileByDate);
        Assert.Equal(20, contract.DaysRemaining);
        Assert.Equal("Warning", contract.ClockStatus);
        Assert.Equal("T10", Assert.Single(contract.DueAlerts).Kind);
        Assert.All(contract.TbcIndicators, t => Assert.Equal("Tbc", t.Status));
        Assert.Contains("not Legal-confirmed", contract.DeterministicExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Legally confirmed", contract.DeterministicExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tbc_catalog_is_server_generated_and_windows_are_not_legal_confirmed()
    {
        var contract = A4ChatSafeAssembler.Assemble(new A4ChatSafeFacts("dealer:11m"));
        var ids = contract.TbcIndicators.Select(t => t.Id).ToArray();
        Assert.Equal(
            [
                "NoticeWindow",
                "CureWindow",
                "FilingWindow",
                "ClockAnchor",
                "PresentationDateDefinition",
                "QualifyingReturnReasonMapping",
                "PaymentAfterDemandNotice",
                "EnforceableDebtDefinition",
                "AlertOwner",
                "AlertTimingDefinition",
                "DemandNoticeSourceOfRecord",
                "ReadyS138Mapping",
                "ChequeNewestSortKey",
                "R6Section138Applicability",
                "A2ToSection138WorkflowRouting"
            ],
            ids);
        Assert.All(contract.TbcIndicators, t => Assert.Equal("Tbc", t.Status));
        Assert.All(contract.TbcIndicators, t => Assert.DoesNotContain("Legally confirmed", t.Detail, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(contract.TbcIndicators, t => t.Id == "NoticeWindow" && t.Detail.Contains("not Legal-confirmed", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("30 / 15 / 30", string.Join(" | ", contract.TbcIndicators.Select(t => t.Detail)));
        Assert.DoesNotContain(typeof(A4ChatSafeFacts).GetProperties().Select(p => p.Name), n => n == "TbcIndicators");
    }

    [Fact]
    public void Missing_demand_notice_does_not_fabricate_cure_or_file_by()
    {
        var contract = A4ChatSafeAssembler.Assemble(new A4ChatSafeFacts(
            "dealer:11m",
            Eligible: true,
            NoticeByDate: new DateOnly(2026, 1, 31),
            CureEndsDate: null,
            FileByDate: null,
            AuthoritativeExposurePresent: true,
            DemandNoticePresent: false,
            EligibilityEvaluated: true));

        Assert.Null(contract.CureEndsDate);
        Assert.Null(contract.FileByDate);
        Assert.Equal(A4FactAvailability.Unavailable, contract.DataCompleteness.Single(c => c.Fact == "DemandNotice").Status);
        Assert.Contains(contract.TbcIndicators, t => t.Id == "DemandNoticeSourceOfRecord");
    }

    [Fact]
    public void Recovery_state_copy_does_not_recalculate_clock()
    {
        var state = new RecoveryState
        {
            CycleId = new CycleId("C1"),
            DealerUrn = new DealerUrn("dealer:11m"),
            AsOf = new DateOnly(2026, 1, 25),
            CorrelationId = new CorrelationId("corr"),
            Mode = RunMode.Shadow,
            Eligibility = new EligibilityVerdict(false, [], "Return reason 'SIGNATURE_MISMATCH' is not in the qualifying set."),
            Clock = new LimitationClock(
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 31),
                null,
                null,
                null,
                6,
                ClockStatus.Warning),
            SelectedChequeNumber = "CHQ-9001",
            SelectedChequeStatus = "Bounced",
            MemoReasonCode = "SIGNATURE_MISMATCH"
        };

        var contract = A4ChatSafeAssembler.FromRecoveryState(state);
        Assert.False(contract.Eligible);
        Assert.Equal("Return reason 'SIGNATURE_MISMATCH' is not in the qualifying set.", contract.BlockReason);
        Assert.Equal("CHQ-9001", contract.SelectedChequeNumber);
        Assert.Equal("SIGNATURE_MISMATCH", contract.MemoReasonCode);
        Assert.Equal(new DateOnly(2026, 1, 31), contract.NoticeByDate);
        Assert.Equal(6, contract.DaysRemaining);
        Assert.Null(contract.CureEndsDate);
        Assert.Empty(contract.DueAlerts);
    }

    [Fact]
    public void Fail_closed_does_not_evaluate_eligibility_or_invent_dates()
    {
        var contract = A4ChatSafeAssembler.FailClosed(
            "dealer:11m",
            "Authoritative A1 exposure was not available. Section 138 eligibility was not evaluated.");

        Assert.Null(contract.Eligible);
        Assert.Contains("not available", contract.BlockReason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(contract.NoticeByDate);
        Assert.Null(contract.FileByDate);
        Assert.False(contract.Provenance.Complete);
        Assert.True(contract.ProductionLegalBlockedOnApprovedStoredProcedure);
        Assert.Contains("LedgerRepository.ListByDealerAsync", contract.ProductionStoredProcedureBlockers);
        Assert.Equal(A4FactAvailability.Unavailable, contract.DataCompleteness.Single(c => c.Fact == "Eligibility").Status);
    }

    [Fact]
    public void Block_reason_is_not_eligibility_record_tostring()
    {
        var verdict = new EligibilityVerdict(false, [], "Return reason 'SIGNATURE_MISMATCH' is not in the qualifying set.");
        Assert.Equal(
            "Return reason 'SIGNATURE_MISMATCH' is not in the qualifying set.",
            A4ChatSafeAssembler.DeterministicBlockReason(verdict));
        Assert.NotEqual(verdict.ToString(), A4ChatSafeAssembler.DeterministicBlockReason(verdict));
    }

    [Fact]
    public void Enforceable_debt_tbc_does_not_bind_odos_os_or_limit()
    {
        var contract = A4ChatSafeAssembler.Assemble(new A4ChatSafeFacts("dealer:11m"));
        var debt = contract.TbcIndicators.Single(t => t.Id == "EnforceableDebtDefinition");
        Assert.Contains("not bound to R2", debt.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("od_os_amt_updt", contract.DeterministicExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("business-line limit", File.ReadAllText(R2Path()), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("od_os_amt_updt", File.ReadAllText(R2Path()), StringComparison.OrdinalIgnoreCase);
    }

    private static string R2Path()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ARC.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "ARC.Domain", "Rules", "R2Section138EligibilityRule.cs");
    }

    [Fact]
    public void R2_still_blocks_non_qualifying_return_code()
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
        Assert.Contains("SIGNATURE_MISMATCH", verdict.BlockReason);
    }
}

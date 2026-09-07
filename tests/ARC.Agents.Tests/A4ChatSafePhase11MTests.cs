using Microsoft.Extensions.DependencyInjection;
using ARC.Agents.A4LegalEligibility;
using ARC.Agents.Context;
using ARC.Agents.Models;
using ARC.Agents.Prompts;
using ARC.Agents.Tests.Support;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.Rules;
using ARC.Domain.ValueObjects;

namespace ARC.Agents.Tests;

public sealed class A4ChatSafePhase11MTests
{
    [Fact]
    public async Task Agent_chat_safe_copies_facts_and_server_tbc_survives_narration()
    {
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "dealer:11m";
        store.SeedDealer(Dealer(urn));
        store.SeedCheque(new SecurityCheque(
            new DealerUrn(urn), "CHQ-9001", new Money(100_000m), ChequeStatus.Bounced,
            depositDate: new DateOnly(2026, 1, 1), validityEnd: new DateOnly(2027, 1, 1)));
        store.SeedMemo(new ChequeReturnMemo(new DealerUrn(urn), "CHQ-9001", "FUNDS_INSUFFICIENT", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)));

        var result = await host.GetRequiredService<LegalEligibilityAgent>().RunAsync(
            new LegalEligibilityAgentRequest(
                urn,
                Exposure(urn),
                null,
                new AgentContext(new DateOnly(2026, 1, 25), "2026-01-11m", "corr-11m", urn)),
            CancellationToken.None);

        Assert.True(result.Facts.Eligibility.Eligible);
        Assert.Equal(result.Facts.Eligibility.Eligible, result.ChatSafe.Eligible);
        Assert.Equal("CHQ-9001", result.ChatSafe.SelectedChequeNumber);
        Assert.Equal("FUNDS_INSUFFICIENT", result.ChatSafe.MemoReasonCode);
        Assert.Equal(result.Facts.Clock?.NoticeByDate, result.ChatSafe.NoticeByDate);
        Assert.All(result.ChatSafe.TbcIndicators, t => Assert.Equal("Tbc", t.Status));
        Assert.Equal(A4ChatSafeAssembler.CentralTbcCatalog().Select(t => t.Id), result.ChatSafe.TbcIndicators.Select(t => t.Id));
        Assert.Contains("must not remove TBC", AgentPrompts.A4, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must not change eligibility", AgentPrompts.A4, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.ChatSafe.ProductionLegalBlockedOnApprovedStoredProcedure);
        Assert.Null(result.ChatSafe.CureEndsDate);
        Assert.Null(result.ChatSafe.FileByDate);
    }

    [Fact]
    public async Task Non_qualifying_memo_still_blocks_before_g3()
    {
        var (services, store) = AgentTestHost.Create();
        using var host = services;
        var urn = "dealer:11m-nq";
        store.SeedDealer(Dealer(urn));
        store.SeedCheque(new SecurityCheque(new DealerUrn(urn), "CHQ-1", new Money(100_000m), ChequeStatus.Bounced, validityEnd: new DateOnly(2027, 1, 1)));
        store.SeedMemo(new ChequeReturnMemo(new DealerUrn(urn), "CHQ-1", "SIGNATURE_MISMATCH", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)));

        var result = await host.GetRequiredService<LegalEligibilityAgent>().RunAsync(
            new LegalEligibilityAgentRequest(urn, Exposure(urn), null, Ctx(urn)),
            CancellationToken.None);

        Assert.False(result.Facts.Eligibility.Eligible);
        Assert.False(result.ChatSafe.Eligible);
        Assert.Equal(result.Facts.Eligibility.BlockReason, result.ChatSafe.BlockReason);
        Assert.Contains("SIGNATURE_MISMATCH", result.ChatSafe.BlockReason);
        Assert.NotEqual(result.Facts.Eligibility.ToString(), result.ChatSafe.BlockReason);
    }

    [Fact]
    public void G3_remains_human_legal_gate_and_agent_cannot_approve()
    {
        Assert.Contains("must not approve legal progression gate G3", AgentPrompts.A4, StringComparison.OrdinalIgnoreCase);
        Assert.False(R4SegregationOfDuties.CanApprove(ActorRole.Agent));
        Assert.Throws<ARC.Domain.Exceptions.InvalidGateDecisionException>(() =>
            GateDecision.Create(
                GateId.LegalProgression,
                "agent@system",
                ActorRole.Agent,
                GateDecisionStatus.Approved,
                "ok",
                CorrelationId.New()));
    }

    private static Dealer Dealer(string urn)
        => new(new DealerUrn(urn), false, "SAP-1", "PORTAL-1", "Mumbai", "West", "tsi@paintco.local");

    private static ExposureBreakdown Exposure(string urn)
        => MetricContract.Compute(
            new DealerUrn(urn),
            new DateOnly(2026, 1, 25),
            new Money(100_000m),
            Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero,
            [new LineItemRef("SAP-FI-AR", "BSEG", "INV-1", 100_000m, new DateOnly(2025, 11, 15))],
            fullyReconciled: true);

    private static AgentContext Ctx(string urn)
        => new(new DateOnly(2026, 1, 25), "2026-01-11m", "corr-11m", urn);
}

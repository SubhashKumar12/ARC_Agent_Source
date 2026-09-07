using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;

namespace ARC.Domain.Tests;

public sealed class A8ChatSafeContractTests
{
    [Fact]
    public void Contract_copies_deterministic_exception_facts()
    {
        var gate = GateDecision.Create(
            GateId.DepotManager,
            "depot.manager@paintco.local",
            ActorRole.DepotManager,
            GateDecisionStatus.Approved,
            "ok",
            new CorrelationId("corr-gate"));

        var contract = A8ChatSafeAssembler.Assemble(new A8ChatSafeFacts(
            "2026-03-11k",
            "North",
            "DEL",
            [new A8ExceptionSnapshot("DEALER-NORTH", "WaitingForHuman", "DepotManager")],
            [new A8DealerSnapshot(
                "DEALER-NORTH",
                nameof(WorkflowStatus.WaitingForHuman),
                "DepotManager",
                "North",
                "DEL",
                [A8ChatSafeAssembler.FromGate(gate)])]));

        var row = Assert.Single(contract.Exceptions);
        Assert.Equal("2026-03-11k", row.CycleId);
        Assert.Equal("DEALER-NORTH", row.DealerUrn);
        Assert.Equal("WaitingForHuman", row.ExceptionKind);
        Assert.Equal("DepotManager", row.ExceptionDetail);
        Assert.Equal("WaitingForHuman", row.WorkflowStatus);
        Assert.Equal("DepotManager", row.WaitingGate);
        Assert.Equal("North", row.Region);
        Assert.Equal("DEL", row.Depot);
        Assert.Equal("DepotManager", Assert.Single(row.Gates).Gate);
        Assert.Equal("ok", Assert.Single(row.Gates).Reason);
    }

    [Fact]
    public void Tbc_indicators_are_server_generated_and_cannot_be_supplied()
    {
        var facts = new A8ChatSafeFacts("C1", "West", null, [], []);
        var contract = A8ChatSafeAssembler.Assemble(facts);
        var ids = contract.TbcIndicators.Select(t => t.Id).ToArray();

        Assert.Equal(
            [
                "AsmRoleDefinition",
                "HoRoleDefinition",
                "AsmHoCoverage",
                "LeverEffectivenessFormula",
                "LearningLoopRequirement",
                "BrokenPtpDefinition",
                "OverdueGateDefinition",
                "HoldReconcileSupervision",
                "OverrideEffectivenessFormula"
            ],
            ids);
        Assert.All(contract.TbcIndicators, t => Assert.Equal("Tbc", t.Status));
        Assert.Equal(A8ChatSafeAssembler.CentralTbcCatalog().Select(t => t.Id), ids);
    }

    [Fact]
    public void Missing_gate_information_is_not_fabricated()
    {
        var contract = A8ChatSafeAssembler.Assemble(new A8ChatSafeFacts(
            "C1",
            null,
            null,
            [new A8ExceptionSnapshot("dealer:x", "Blocked", "halted")],
            [new A8DealerSnapshot("dealer:x", "Blocked", null, null, null, [])]));

        var dealer = Assert.Single(contract.Dealers);
        Assert.Empty(dealer.Gates);
        Assert.Null(dealer.WaitingGate);
        Assert.Null(dealer.Region);
        Assert.Null(dealer.Depot);
        Assert.Equal("Blocked", contract.Exceptions.Single().ExceptionKind);
        Assert.Empty(contract.Exceptions.Single().Gates);
    }

    [Fact]
    public void Production_sp_blockers_are_the_11c_a8_reads()
    {
        var contract = A8ChatSafeAssembler.Assemble(new A8ChatSafeFacts("C1", "North", null, [], []));
        Assert.True(contract.ProductionSupervisionBlockedOnApprovedStoredProcedure);
        Assert.Equal(
            [
                "DealerRepository.ListByRegionAsync",
                "RecoveryCaseRepository.GetAsync",
                "RecoveryCaseRepository.ListRankedWorklistAsync",
                "GateDecisionRepository.ListAsync"
            ],
            contract.ProductionStoredProcedureBlockers);
    }

    [Fact]
    public void No_lever_effectiveness_or_learning_metric_is_emitted()
    {
        var contract = A8ChatSafeAssembler.Assemble(new A8ChatSafeFacts("C1", "North", "DEL", [], []));
        Assert.DoesNotContain(contract.DataCompleteness, c => c.Status == A8FactAvailability.Available && c.Fact == "LeverEffectiveness");
        Assert.Equal(A8FactAvailability.Unavailable, contract.DataCompleteness.Single(c => c.Fact == "LeverEffectiveness").Status);
        Assert.Equal(A8FactAvailability.Unavailable, contract.DataCompleteness.Single(c => c.Fact == "LearningStore").Status);
        Assert.Equal(A8FactAvailability.Unavailable, contract.DataCompleteness.Single(c => c.Fact == "ChaseStatus").Status);
        Assert.Equal(A8FactAvailability.Unavailable, contract.DataCompleteness.Single(c => c.Fact == "BrokenPtp").Status);
        Assert.DoesNotContain(contract.DeterministicExplanation, "conversion rate", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(contract.DeterministicExplanation, "success rate", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TBC", contract.DeterministicExplanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Actor_role_enum_does_not_include_asm_or_ho()
    {
        var names = Enum.GetNames<ActorRole>();
        Assert.DoesNotContain(names, n => n.Equals("Asm", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Equals("Ho", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Equals("ASM", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Equals("HO", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("DepotManager", names);
        Assert.Contains("Tsi", names);
        Assert.Equal(7, names.Length);
    }

    [Fact]
    public void Gate_metadata_does_not_invent_sla_approver_or_escalation()
    {
        var names = typeof(A8GateMetadata).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain(names, n => n.Contains("Sla", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Equals("Approver", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("ExpiryPolicy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Escalation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("ActorUpn", names);
        Assert.Contains("WasOverride", names);
    }

    [Fact]
    public void No_learning_store_or_lever_effectiveness_metric_type_was_added()
    {
        var names = typeof(A8ChatSafeContract).Assembly.GetTypes().Select(t => t.Name).ToArray();
        Assert.DoesNotContain(names, n => n.Contains("LearningStore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("LearningLoop", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("LeverEffectivenessMetric", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(A8ChatSafeFacts).GetProperties().Select(p => p.Name), n => n == "TbcIndicators");
        Assert.DoesNotContain(typeof(A8ChatSafeFacts).GetProperties().Select(p => p.Name), n => n == "DataCompleteness");
    }

    [Fact]
    public void Broken_ptp_is_not_redefined_when_caller_did_not_supply_ptps()
    {
        var contract = A8ChatSafeAssembler.Assemble(new A8ChatSafeFacts(
            "C1",
            "North",
            null,
            [],
            [],
            BrokenPtpSuppliedByCaller: false));

        Assert.Equal(A8FactAvailability.Unavailable, contract.DataCompleteness.Single(c => c.Fact == "BrokenPtp").Status);
        Assert.Contains(contract.TbcIndicators, t => t.Id == "BrokenPtpDefinition" && t.Status == "Tbc");
        Assert.Contains("not redefined", contract.TbcIndicators.Single(t => t.Id == "BrokenPtpDefinition").Detail, StringComparison.OrdinalIgnoreCase);
    }
}

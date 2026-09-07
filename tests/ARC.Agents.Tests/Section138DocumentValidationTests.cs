using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using ARC.Agents.A4LegalEligibility;
using ARC.Agents.Tests.Fakes;
using ARC.Agents.Tests.Support;
using ARC.Agents.Workflows.Executors;
using ARC.Agents.Workflows.Models;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;
using ARC.Knowledge.Documents;
using ARC.Tools.Evidence;

namespace ARC.Agents.Tests;

public sealed class Section138DocumentValidationTests
{
    [Fact]
    public async Task Document_validation_runs_before_A4_and_can_block_without_eligibility()
    {
        var created = AgentTestHost.Create();
        using var services = created.Services;
        var store = created.Store;
        var spy = new SpyOrchestrator { CanProgress = false, Reason = "Document extraction requires Depot Admin review (keyed mismatch)." };
        ReplaceOrchestrator(services, spy);

        var urn = "dealer:s3";
        store.SeedDealer(new Dealer(new DealerUrn(urn), false, "SAP-1", "P-1", "Depot", "West", "tsi@x"));
        store.SeedLedger(new LedgerPosition(
            new DealerUrn(urn),
            "Invoice",
            new DateOnly(2025, 12, 1),
            new DateOnly(2025, 11, 15),
            new Money(100_000m),
            new LineItemRef("SAP-FI-AR", "BSEG", "INV-1", 100_000m, new DateOnly(2025, 11, 15))));
        store.SeedCheque(new SecurityCheque(new DealerUrn(urn), "CHQ-9001", new Money(100_000m), ChequeStatus.Bounced, "400002000"));
        store.SeedMemo(new ChequeReturnMemo(new DealerUrn(urn), "CHQ-9001", "FUNDS_INSUFFICIENT", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)));

        var executors = services.GetRequiredService<ArcWorkflowExecutors>();
        var message = new WorkflowMessage
        {
            Kind = ArcWorkflowKind.Section138,
            State = new RecoveryState
            {
                CycleId = new CycleId("2026-01-s3"),
                DealerUrn = new DealerUrn(urn),
                AsOf = new DateOnly(2026, 1, 25),
                CorrelationId = new CorrelationId("corr-s3"),
                Mode = RunMode.Shadow,
                Exposure = MetricContract.Compute(
                    new DealerUrn(urn),
                    new DateOnly(2026, 1, 25),
                    new Money(100_000m),
                    Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero,
                    [new LineItemRef("SAP-FI-AR", "BSEG", "INV-1", 100_000m, new DateOnly(2025, 11, 15))],
                    true)
            },
            Evidence = [new EvidenceItem(DocumentType.SecurityChequeImage, "legal-worm/dealer:s3/SecurityChequeImage.pdf")]
        };

        var result = await executors.A4Async(message, NullWorkflowContext.Instance, CancellationToken.None);

        Assert.Equal(1, spy.Calls);
        Assert.Equal(WorkflowStatus.Blocked, result.State.Status);
        Assert.Null(result.State.Eligibility);
        Assert.Contains("Depot Admin", result.State.TerminationReason);
    }

    [Fact]
    public async Task A4_uses_sql_facts_after_successful_document_validation()
    {
        var created = AgentTestHost.Create();
        using var services = created.Services;
        var store = created.Store;
        var urn = "dealer:s3";
        store.SeedDealer(new Dealer(new DealerUrn(urn), false, "SAP-1", "P-1", "Depot", "West", "tsi@x"));
        store.SeedLedger(new LedgerPosition(
            new DealerUrn(urn),
            "Invoice",
            new DateOnly(2025, 12, 1),
            new DateOnly(2025, 11, 15),
            new Money(100_000m),
            new LineItemRef("SAP-FI-AR", "BSEG", "INV-1", 100_000m, new DateOnly(2025, 11, 15))));
        store.SeedCheque(new SecurityCheque(
            new DealerUrn(urn), "CHQ-9001", new Money(100_000m), ChequeStatus.Bounced, "400002000",
            new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)));
        store.SeedMemo(new ChequeReturnMemo(new DealerUrn(urn), "CHQ-9001", "FUNDS_INSUFFICIENT", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)));
        store.SeedEvidence("legal-worm/dealer:s3/SecurityChequeImage.pdf");

        var spy = new SpyOrchestrator { CanProgress = true, Reason = "ok" };
        ReplaceOrchestrator(services, spy);

        var executors = services.GetRequiredService<ArcWorkflowExecutors>();
        var message = new WorkflowMessage
        {
            Kind = ArcWorkflowKind.Section138,
            State = new RecoveryState
            {
                CycleId = new CycleId("2026-01-s3"),
                DealerUrn = new DealerUrn(urn),
                AsOf = new DateOnly(2026, 1, 25),
                CorrelationId = new CorrelationId("corr-s3"),
                Mode = RunMode.Shadow,
                Exposure = MetricContract.Compute(
                    new DealerUrn(urn),
                    new DateOnly(2026, 1, 25),
                    new Money(100_000m),
                    Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero,
                    [new LineItemRef("SAP-FI-AR", "BSEG", "INV-1", 100_000m, new DateOnly(2025, 11, 15))],
                    true)
            },
            Evidence = [new EvidenceItem(DocumentType.SecurityChequeImage, "legal-worm/dealer:s3/SecurityChequeImage.pdf")]
        };

        var result = await executors.A4Async(message, NullWorkflowContext.Instance, CancellationToken.None);

        Assert.Equal(1, spy.Calls);
        Assert.NotNull(result.State.Eligibility);
        Assert.True(result.State.Eligibility!.Eligible);
        Assert.Equal("CHQ-9001", result.Cheque?.ChequeNumber);
        Assert.Equal("FUNDS_INSUFFICIENT", result.Memo?.ReturnReasonCode);
    }

    [Fact]
    public void Production_host_registers_demo_document_intelligence_without_endpoint()
    {
        using var host = AgentTestHost.Create().Services;
        Assert.IsType<DemoDocumentIntelligenceService>(host.GetRequiredService<IDocumentIntelligenceService>());
        Assert.NotNull(host.GetRequiredService<LegalEligibilityAgent>());
    }

    private static void ReplaceOrchestrator(ServiceProvider services, SpyOrchestrator spy)
    {
        var field = typeof(ArcWorkflowExecutors).GetField("_documentExtraction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(services.GetRequiredService<ArcWorkflowExecutors>(), spy);
    }

    private sealed class SpyOrchestrator : IDocumentExtractionOrchestrator
    {
        public int Calls { get; private set; }
        public bool CanProgress { get; init; }
        public string Reason { get; init; } = "";

        public Task<DocumentValidationResult> ValidateForSection138Async(
            DealerUrn dealerUrn,
            IReadOnlyList<ExtractionDocumentRef> evidence,
            string? correlationId,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new DocumentValidationResult
            {
                CanProgress = CanProgress,
                Reason = Reason
            });
        }
    }

    private sealed class NullWorkflowContext : IWorkflowContext
    {
        public static readonly NullWorkflowContext Instance = new();
        public bool ConcurrentRunsEnabled => false;
        public IReadOnlyDictionary<string, string>? TraceContext => null;
        public ValueTask AddEventAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default) => default;
        public ValueTask<T?> ReadStateAsync<T>(string key, string? scopeName = null, CancellationToken cancellationToken = default) => default;
        public ValueTask<T> ReadOrInitStateAsync<T>(string key, Func<T> initialStateFactory, string? scopeName = null, CancellationToken cancellationToken = default)
            => new(initialStateFactory());
        public ValueTask QueueStateUpdateAsync<T>(string key, T? value, string? scopeName = null, CancellationToken cancellationToken = default) => default;
        public ValueTask QueueClearScopeAsync(string? scopeName = null, CancellationToken cancellationToken = default) => default;
        public ValueTask SendMessageAsync<T>(T message, string? targetId = null, CancellationToken cancellationToken = default) => default;
        public ValueTask SendMessageAsync(object message, string? targetId = null, CancellationToken cancellationToken = default) => default;
        public ValueTask YieldOutputAsync(object output, CancellationToken cancellationToken = default) => default;
        public ValueTask RequestHaltAsync() => default;
        public ValueTask<HashSet<string>> ReadStateKeysAsync(string? scopeName = null, CancellationToken cancellationToken = default)
            => new([]);
    }
}

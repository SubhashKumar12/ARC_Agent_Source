using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ARC.Data.Blob;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Documents;
using ARC.Knowledge.Exceptions;

namespace ARC.Knowledge.Tests.Documents;

public sealed class ChequeComparisonTests
{
    [Fact]
    public void Compare_matching_required_fields_is_not_mismatch()
    {
        var extracted = HighConfidenceCheque("CHQ-1", "400002000", 100_000m);
        var keyed = new KeyedChequeFields("CHQ-1", "400002000", 100_000m);

        var discrepancy = ChequeComparison.Compare(extracted, keyed);

        Assert.False(discrepancy.HasMismatch);
    }

    [Fact]
    public void Compare_blank_extracted_cheque_number_is_mismatch_when_keyed()
    {
        var extracted = HighConfidenceCheque(null, "400002000", 100_000m);
        var keyed = new KeyedChequeFields("CHQ-1", "400002000", 100_000m);

        var discrepancy = ChequeComparison.Compare(extracted, keyed);

        Assert.True(discrepancy.ChequeNumberMismatch);
        Assert.True(discrepancy.HasMismatch);
    }

    [Fact]
    public void Compare_blank_extracted_micr_is_mismatch_when_keyed()
    {
        var extracted = HighConfidenceCheque("CHQ-1", null, 100_000m);
        var keyed = new KeyedChequeFields("CHQ-1", "400002000", 100_000m);

        Assert.True(ChequeComparison.Compare(extracted, keyed).MicrMismatch);
    }

    [Fact]
    public void Compare_missing_extracted_amount_is_mismatch_when_keyed()
    {
        var extracted = new ChequeExtraction
        {
            ChequeNumber = "CHQ-1",
            ChequeNumberConfidence = 0.99m,
            MicrLine = "400002000",
            MicrConfidence = 0.99m,
            Amount = null,
            AmountConfidence = 0.99m
        };
        var keyed = new KeyedChequeFields("CHQ-1", "400002000", 100_000m);

        Assert.True(ChequeComparison.Compare(extracted, keyed).AmountMismatch);
    }

    [Fact]
    public void Compare_cheque_number_mismatch()
    {
        var extracted = HighConfidenceCheque("CHQ-2", "400002000", 100_000m);
        var keyed = new KeyedChequeFields("CHQ-1", "400002000", 100_000m);

        Assert.True(ChequeComparison.Compare(extracted, keyed).ChequeNumberMismatch);
    }

    [Fact]
    public void Compare_micr_mismatch()
    {
        var extracted = HighConfidenceCheque("CHQ-1", "111111111", 100_000m);
        var keyed = new KeyedChequeFields("CHQ-1", "400002000", 100_000m);

        Assert.True(ChequeComparison.Compare(extracted, keyed).MicrMismatch);
    }

    [Fact]
    public void Compare_amount_mismatch()
    {
        var extracted = HighConfidenceCheque("CHQ-1", "400002000", 50_000m);
        var keyed = new KeyedChequeFields("CHQ-1", "400002000", 100_000m);

        Assert.True(ChequeComparison.Compare(extracted, keyed).AmountMismatch);
    }

    [Fact]
    public void Threshold_pass_requires_values_and_confidence()
    {
        var options = new ArcKnowledgeOptions();
        Assert.True(HighConfidenceCheque("CHQ-1", "400002000", 1m)
            .MeetsAutoAcceptThreshold(options.ChequeNumberConfidence, options.MicrConfidence, options.AmountConfidence));
    }

    [Fact]
    public void Threshold_fail_when_confidence_below_floor()
    {
        var options = new ArcKnowledgeOptions();
        var extracted = new ChequeExtraction
        {
            ChequeNumber = "CHQ-1",
            ChequeNumberConfidence = 0.50m,
            MicrLine = "400002000",
            MicrConfidence = 0.99m,
            Amount = 1m,
            AmountConfidence = 0.99m
        };

        Assert.False(extracted.MeetsAutoAcceptThreshold(
            options.ChequeNumberConfidence, options.MicrConfidence, options.AmountConfidence));
    }

    [Fact]
    public void Assignment_threshold_defaults_are_configuration_driven()
    {
        var options = new ArcKnowledgeOptions();
        Assert.Equal(0.90m, options.ChequeNumberConfidence);
        Assert.Equal(0.90m, options.MicrConfidence);
        Assert.Equal(0.85m, options.AmountConfidence);
    }

    internal static ChequeExtraction HighConfidenceCheque(string? number, string? micr, decimal? amount)
        => new()
        {
            ChequeNumber = number,
            ChequeNumberConfidence = 0.99m,
            MicrLine = micr,
            MicrConfidence = 0.99m,
            Amount = amount,
            AmountConfidence = 0.99m
        };
}

public sealed class DocumentContentHasherTests
{
    [Fact]
    public void Raw_bytes_hash_is_stable_and_does_not_lowercase_binary()
    {
        var a = DocumentContentHasher.ComputeSha256("ABC"u8);
        var b = DocumentContentHasher.ComputeSha256("ABC"u8);
        var c = DocumentContentHasher.ComputeSha256("abc"u8);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(64, a.Length);
    }
}

public sealed class DocumentIntelligenceRegistrationTests
{
    [Fact]
    public void Azure_service_constructs_without_endpoint_and_does_not_call_azure()
    {
        var service = new DocumentIntelligenceService(
            Options.Create(new ArcKnowledgeOptions()),
            NullLogger<DocumentIntelligenceService>.Instance);

        Assert.NotNull(service);
    }
}

public sealed class DemoDocumentIntelligenceServiceTests
{
    [Fact]
    public async Task Demo_maps_cheque_date_from_keyed_deposit_date_without_azure()
    {
        var store = new SeededCheques();
        var demo = new DemoDocumentIntelligenceService(store, Options.Create(new ArcKnowledgeOptions()));

        var result = await demo.ExtractAsync(
            new MemoryStream("demo"u8.ToArray()),
            new DocumentMetadata(
                "dealer:s3/SecurityChequeImage",
                DocumentType.SecurityChequeImage,
                "legal-worm/dealer:s3/SecurityChequeImage.pdf",
                "prebuilt-check.us",
                "ACTIVE",
                null,
                store.Urn,
                "corr-1",
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(ExtractionStatus.Succeeded, result.Status);
        Assert.Equal(new DateOnly(2026, 1, 1), result.Cheque?.ChequeDate);
        Assert.Equal("CHQ-9001", result.Cheque?.ChequeNumber);
    }

    private sealed class SeededCheques : IChequeRepository
    {
        public DealerUrn Urn { get; } = new("dealer:s3");

        public Task<IReadOnlyList<SecurityCheque>> ListChequesAsync(DealerUrn urn, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SecurityCheque>>([
                new SecurityCheque(Urn, "CHQ-9001", new Money(100_000m), ChequeStatus.Bounced, "400002000",
                    new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1))
            ]);

        public Task<IReadOnlyList<ChequeReturnMemo>> ListReturnMemosAsync(DealerUrn urn, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ChequeReturnMemo>>([
                new ChequeReturnMemo(Urn, "CHQ-9001", "FUNDS_INSUFFICIENT", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1))
            ]);
    }
}

public sealed class DocumentExtractionOrchestratorTests
{
    [Fact]
    public async Task Successful_cheque_extraction_allows_progress()
    {
        var (orch, intel, _, _) = Create();
        SeedCheque(intel.Store);
        intel.Store.SeedEvidence("legal-worm/dealer:s3/SecurityChequeImage.pdf");

        var result = await orch.ValidateForSection138Async(
            new DealerUrn("dealer:s3"),
            [new ExtractionDocumentRef(DocumentType.SecurityChequeImage, "legal-worm/dealer:s3/SecurityChequeImage.pdf")],
            "corr-1",
            CancellationToken.None);

        Assert.True(result.CanProgress);
        Assert.Equal(1, intel.Calls);
        Assert.False(result.UsedCache);
    }

    [Fact]
    public async Task Cache_hit_does_not_call_intelligence_again()
    {
        var (orch, intel, _, _) = Create();
        SeedCheque(intel.Store);
        intel.Store.SeedEvidence("legal-worm/dealer:s3/SecurityChequeImage.pdf");
        var refs = new ExtractionDocumentRef[]
        {
            new(DocumentType.SecurityChequeImage, "legal-worm/dealer:s3/SecurityChequeImage.pdf")
        };
        var urn = new DealerUrn("dealer:s3");

        await orch.ValidateForSection138Async(urn, refs, "c1", CancellationToken.None);
        await orch.ValidateForSection138Async(urn, refs, "c2", CancellationToken.None);

        Assert.Equal(1, intel.Calls);
    }

    [Fact]
    public async Task Model_change_forces_reprocessing()
    {
        var (orch, intel, store, options) = Create();
        SeedCheque(intel.Store);
        intel.Store.SeedEvidence("legal-worm/dealer:s3/SecurityChequeImage.pdf");
        var refs = new ExtractionDocumentRef[]
        {
            new(DocumentType.SecurityChequeImage, "legal-worm/dealer:s3/SecurityChequeImage.pdf")
        };
        var urn = new DealerUrn("dealer:s3");

        await orch.ValidateForSection138Async(urn, refs, "c1", CancellationToken.None);
        options.ChequeModelId = "prebuilt-layout";
        await orch.ValidateForSection138Async(urn, refs, "c2", CancellationToken.None);

        Assert.Equal(2, intel.Calls);
        Assert.NotNull(await store.GetAsync(
            DocumentContentHasher.ComputeSha256(System.Text.Encoding.UTF8.GetBytes(refs[0].Location)),
            "prebuilt-layout",
            CancellationToken.None));
    }

    [Fact]
    public async Task Low_confidence_blocks_progress()
    {
        var (orch, intel, _, _) = Create();
        SeedCheque(intel.Store);
        intel.Store.SeedEvidence("path/cheque.pdf");
        intel.ChequeOverride = new ChequeExtraction
        {
            ChequeNumber = "CHQ-9001",
            ChequeNumberConfidence = 0.40m,
            MicrLine = "400002000",
            MicrConfidence = 0.99m,
            Amount = 100_000m,
            AmountConfidence = 0.99m
        };

        var result = await orch.ValidateForSection138Async(
            new DealerUrn("dealer:s3"),
            [new ExtractionDocumentRef(DocumentType.SecurityChequeImage, "path/cheque.pdf")],
            "c",
            CancellationToken.None);

        Assert.False(result.CanProgress);
        Assert.Contains("low confidence", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Azure_failure_blocks_without_sensitive_reason()
    {
        var (orch, intel, _, _) = Create();
        SeedCheque(intel.Store);
        intel.Store.SeedEvidence("path/cheque.pdf");
        intel.ThrowOnExtract = true;

        var result = await orch.ValidateForSection138Async(
            new DealerUrn("dealer:s3"),
            [new ExtractionDocumentRef(DocumentType.SecurityChequeImage, "path/cheque.pdf")],
            "c",
            CancellationToken.None);

        Assert.False(result.CanProgress);
        Assert.DoesNotContain("400002000", result.Reason);
        Assert.DoesNotContain("CHQ-9001", result.Reason);
        Assert.DoesNotContain("100000", result.Reason);
    }

    [Fact]
    public async Task Unsupported_document_blocks_for_required_cheque()
    {
        var (orch, intel, _, _) = Create();
        SeedCheque(intel.Store);
        intel.Store.SeedEvidence("path/invoice.pdf");
        intel.ForceUnsupported = true;

        var result = await orch.ValidateForSection138Async(
            new DealerUrn("dealer:s3"),
            [new ExtractionDocumentRef(DocumentType.SecurityChequeImage, "path/invoice.pdf")],
            "c",
            CancellationToken.None);

        Assert.False(result.CanProgress);
    }

    [Fact]
    public async Task Memo_reason_discrepancy_blocks_and_does_not_change_sql()
    {
        var (orch, intel, _, _) = Create();
        SeedCheque(intel.Store);
        intel.Store.SeedEvidence("path/memo.pdf");
        intel.MemoOverride = new ReturnMemoExtraction("other", "SIGNATURE_MISMATCH", 0.99m, new DateOnly(2026, 1, 1), null);

        var result = await orch.ValidateForSection138Async(
            new DealerUrn("dealer:s3"),
            [new ExtractionDocumentRef(DocumentType.ChequeReturnMemo, "path/memo.pdf")],
            "c",
            CancellationToken.None);

        Assert.False(result.CanProgress);
        var sql = await intel.Store.ListReturnMemosAsync(new DealerUrn("dealer:s3"), CancellationToken.None);
        Assert.Equal("FUNDS_INSUFFICIENT", sql[0].ReturnReasonCode);
    }

    [Fact]
    public async Task Logs_do_not_contain_sensitive_fields()
    {
        var logger = new CollectingLogger();
        var store = new InMemoryChequeEvidence();
        SeedCheque(store);
        store.SeedEvidence("path/cheque.pdf");
        var options = Options.Create(new ArcKnowledgeOptions());
        var intel = new ScriptedIntelligence(store) { ChequeOverride = ChequeComparisonTests.HighConfidenceCheque("CHQ-9001", "400002000", 100_000m) };
        var orch = new DocumentExtractionOrchestrator(
            intel, store, store, new InMemoryDocumentExtractionStore(), options, logger);

        await orch.ValidateForSection138Async(
            new DealerUrn("dealer:s3"),
            [new ExtractionDocumentRef(DocumentType.SecurityChequeImage, "path/cheque.pdf")],
            "corr-secret",
            CancellationToken.None);

        var joined = string.Join('\n', logger.Messages);
        Assert.DoesNotContain("400002000", joined);
        Assert.DoesNotContain("CHQ-9001", joined);
        Assert.DoesNotContain("100000", joined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("corr-secret", joined);
    }

    [Fact]
    public async Task Blank_extracted_required_field_blocks_progress()
    {
        var (orch, intel, _, _) = Create();
        SeedCheque(intel.Store);
        intel.Store.SeedEvidence("path/cheque.pdf");
        intel.ChequeOverride = ChequeComparisonTests.HighConfidenceCheque(null, "400002000", 100_000m);

        var result = await orch.ValidateForSection138Async(
            new DealerUrn("dealer:s3"),
            [new ExtractionDocumentRef(DocumentType.SecurityChequeImage, "path/cheque.pdf")],
            "c",
            CancellationToken.None);

        Assert.False(result.CanProgress);
        Assert.Contains("missing required field", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Empty_evidence_skips_validation_so_A4_can_use_sql()
    {
        var (orch, _, _, _) = Create();
        var result = await orch.ValidateForSection138Async(
            new DealerUrn("dealer:s4"),
            [],
            "c",
            CancellationToken.None);
        Assert.True(result.CanProgress);
    }

    private static (DocumentExtractionOrchestrator Orch, ScriptedIntelligence Intel, InMemoryDocumentExtractionStore Store, ArcKnowledgeOptions Options) Create()
    {
        var evidence = new InMemoryChequeEvidence();
        var options = new ArcKnowledgeOptions();
        var intel = new ScriptedIntelligence(evidence);
        var cache = new InMemoryDocumentExtractionStore();
        var orch = new DocumentExtractionOrchestrator(
            intel,
            evidence,
            evidence,
            cache,
            Options.Create(options),
            NullLogger<DocumentExtractionOrchestrator>.Instance);
        return (orch, intel, cache, options);
    }

    private static void SeedCheque(InMemoryChequeEvidence store)
    {
        var urn = new DealerUrn("dealer:s3");
        store.SeedCheque(new SecurityCheque(urn, "CHQ-9001", new Money(100_000m), ChequeStatus.Bounced, "400002000", new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)));
        store.SeedMemo(new ChequeReturnMemo(urn, "CHQ-9001", "FUNDS_INSUFFICIENT", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)));
    }

    private sealed class ScriptedIntelligence : IDocumentIntelligenceService
    {
        public ScriptedIntelligence(InMemoryChequeEvidence store) => Store = store;
        public InMemoryChequeEvidence Store { get; }
        public int Calls { get; private set; }
        public bool ThrowOnExtract { get; set; }
        public bool ForceUnsupported { get; set; }
        public ChequeExtraction? ChequeOverride { get; set; }
        public ReturnMemoExtraction? MemoOverride { get; set; }

        public Task<DocumentExtractionResult> ExtractAsync(Stream content, DocumentMetadata metadata, CancellationToken cancellationToken)
        {
            Calls++;
            if (ForceUnsupported)
                throw new UnsupportedDocumentException(metadata.DocumentType.ToString());
            if (ThrowOnExtract)
                throw new ExtractionFailedException(metadata.DocumentId, new InvalidOperationException("azure down"));

            var cheque = metadata.DocumentType == DocumentType.SecurityChequeImage
                ? ChequeOverride ?? ChequeComparisonTests.HighConfidenceCheque("CHQ-9001", "400002000", 100_000m)
                : null;
            var memo = metadata.DocumentType == DocumentType.ChequeReturnMemo
                ? MemoOverride ?? new ReturnMemoExtraction("FUNDS_INSUFFICIENT", "FUNDS_INSUFFICIENT", 0.99m, new DateOnly(2026, 1, 1), null)
                : null;

            return Task.FromResult(new DocumentExtractionResult
            {
                Metadata = metadata,
                Status = ExtractionStatus.Succeeded,
                Cheque = cheque,
                ReturnMemo = memo
            });
        }

        public ExtractionDiscrepancy CompareCheque(ChequeExtraction extracted, KeyedChequeFields keyed)
            => ChequeComparison.Compare(extracted, keyed);
    }

    private sealed class CollectingLogger : ILogger<DocumentExtractionOrchestrator>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private sealed class InMemoryChequeEvidence : IChequeRepository, IEvidenceDocumentRepository
    {
        private readonly List<SecurityCheque> _cheques = [];
        private readonly List<ChequeReturnMemo> _memos = [];
        private readonly HashSet<string> _evidence = new(StringComparer.Ordinal);

        public void SeedCheque(SecurityCheque cheque) => _cheques.Add(cheque);
        public void SeedMemo(ChequeReturnMemo memo) => _memos.Add(memo);
        public void SeedEvidence(string location) => _evidence.Add(location);

        public Task<IReadOnlyList<SecurityCheque>> ListChequesAsync(DealerUrn urn, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SecurityCheque>>(_cheques.Where(c => c.DealerUrn.Value == urn.Value).ToList());

        public Task<IReadOnlyList<ChequeReturnMemo>> ListReturnMemosAsync(DealerUrn urn, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ChequeReturnMemo>>(_memos.Where(c => c.DealerUrn.Value == urn.Value).ToList());

        public Task<EvidenceDocument> UploadAsync(DealerUrn dealerUrn, DocumentType type, Stream content, string fileName, string contentType, CancellationToken cancellationToken)
        {
            var location = $"evidence/{dealerUrn.Value}/{type}/{fileName}";
            _evidence.Add(location);
            return Task.FromResult(new EvidenceDocument(dealerUrn, type, location));
        }

        public Task<Stream> DownloadAsync(EvidenceDocument document, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(document.Location)));

        public Task<bool> ExistsAsync(EvidenceDocument document, CancellationToken cancellationToken)
            => Task.FromResult(_evidence.Contains(document.Location));

        public Task DeleteAsync(EvidenceDocument document, CancellationToken cancellationToken)
        {
            _evidence.Remove(document.Location);
            return Task.CompletedTask;
        }
    }
}

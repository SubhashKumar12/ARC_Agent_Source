using ARC.Data.Cosmos;
using ARC.Eval.Retrieval;
using ARC.Eval.Retrieval.Live;
using ARC.Eval.Retrieval.Offline;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Embeddings;
using ARC.Knowledge.Exceptions;
using ARC.Knowledge.Provenance;
using ARC.Knowledge.Retrieval;
using ARC.Knowledge.Vector;

namespace ARC.Eval;

public sealed class LiveRetrievalEvaluationTests
{
    [Fact]
    public void Live_eval_skips_safely_when_configuration_is_unavailable()
    {
        var previousEnable = Environment.GetEnvironmentVariable(LiveRetrievalEvalRunner.EnableEnvironmentVariable);
        var previousCosmos = Environment.GetEnvironmentVariable("ARC_COSMOS_CONNECTION_STRING");
        var previousEndpoint = Environment.GetEnvironmentVariable("ARC_EMBEDDINGS_ENDPOINT");
        var previousDeployment = Environment.GetEnvironmentVariable("ARC_EMBEDDINGS_DEPLOYMENT");
        try
        {
            Environment.SetEnvironmentVariable(LiveRetrievalEvalRunner.EnableEnvironmentVariable, "true");
            Environment.SetEnvironmentVariable("ARC_COSMOS_CONNECTION_STRING", null);
            Environment.SetEnvironmentVariable("ARC_EMBEDDINGS_ENDPOINT", null);
            Environment.SetEnvironmentVariable("ARC_EMBEDDINGS_DEPLOYMENT", null);
            var outcome = LiveRetrievalEvalRunner.TryRunFromEnvironment();
            Assert.True(outcome.IsPending);
            Assert.Equal(LiveRetrievalEvalOutcome.PendingStatus, outcome.Status);
            Assert.Null(outcome.Report);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LiveRetrievalEvalRunner.EnableEnvironmentVariable, previousEnable);
            Environment.SetEnvironmentVariable("ARC_COSMOS_CONNECTION_STRING", previousCosmos);
            Environment.SetEnvironmentVariable("ARC_EMBEDDINGS_ENDPOINT", previousEndpoint);
            Environment.SetEnvironmentVariable("ARC_EMBEDDINGS_DEPLOYMENT", previousDeployment);
        }
    }

    [Fact]
    public void Dimension_mismatch_fails_closed_without_silent_conversion()
    {
        var ex = Assert.Throws<KnowledgeException>(
            () => LiveRetrievalDimensionGuard.EnsureAssignmentDimensions(1536));
        Assert.Contains("3072", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Fail closed", ex.Message, StringComparison.OrdinalIgnoreCase);

        var observed = Assert.Throws<KnowledgeException>(
            () => LiveRetrievalDimensionGuard.EnsureAssignmentDimensions(3072, 1536));
        Assert.Contains("1536", observed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_eval_uses_production_retrieval_abstractions_and_does_not_write()
    {
        var writes = new WriteRejectingIndexedDocumentStore();
        var inner = new CapturingLiveRetriever();
        var embeddings = new Fixed3072EmbeddingProvider();

        var outcome = LiveRetrievalEvalRunner.RunWithProductionAbstractions(
            inner,
            embeddings,
            cases: MiniCorpus(),
            runAzureProbes: true);

        Assert.Equal(LiveRetrievalEvalOutcome.RanStatus, outcome.Status);
        Assert.Equal(LiveRetrievalEvalOutcome.CitationFaithfulnessNotMeasured, outcome.CitationFaithfulness);
        Assert.NotNull(outcome.Report);
        Assert.Contains(nameof(KnowledgeRetrievalService), outcome.Report!.Notes, StringComparison.Ordinal);
        Assert.Contains(nameof(CapturingLiveRetriever), outcome.Report.Notes, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(OfflineDocumentRetriever), outcome.Report.Notes, StringComparison.Ordinal);
        Assert.Equal(0, writes.UpsertCount);
        Assert.Throws<InvalidOperationException>(() => writes.UpsertAsync(new IndexedDocument { id = "x", documentType = "Policy" }).GetAwaiter().GetResult());
        Assert.Equal(1, writes.UpsertCount);
        Assert.Equal(0, inner.WriteCount);
        Assert.NotNull(outcome.Azure);
        Assert.True(outcome.Azure!.LexicalRetrievalExecuted);
        Assert.True(outcome.Azure.VectorRetrievalExecuted);
        Assert.True(outcome.Azure.MetadataFilterExecuted);
        Assert.True(outcome.Azure.TopKCapEnforced);
        Assert.Equal(3072, outcome.Azure.VectorContractDimensions);
        Assert.Equal(3072, outcome.Azure.EmbeddingProviderDimensions);
        Assert.Equal(3072, outcome.Azure.ObservedQueryEmbeddingDimensions);
        Assert.All(outcome.RouteDiagnostics, line =>
        {
            Assert.True(
                line.Contains("Lexical", StringComparison.Ordinal)
                || line.Contains("Vector", StringComparison.Ordinal),
                line);
            Assert.DoesNotContain("RRF", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("BM25", line, StringComparison.OrdinalIgnoreCase);
        });
        Assert.All(outcome.Report.Modes, m => Assert.True(m.AvgCosmosQueries <= 1.01));
        Assert.True(inner.LastTopK is null or <= 8);
        Assert.DoesNotContain("AccountEndpoint", outcome.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SharedAccessKey", outcome.Report.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Live_eval_rejects_offline_retriever()
    {
        var outcome = LiveRetrievalEvalRunner.RunWithProductionAbstractions(
            new OfflineDocumentRetriever(SyntheticKnowledgeCorpus.CreateDocuments()),
            new Fixed3072EmbeddingProvider(),
            cases: MiniCorpus(),
            runAzureProbes: false);

        Assert.Equal(LiveRetrievalEvalOutcome.FailedClosedStatus, outcome.Status);
        Assert.Contains("OfflineDocumentRetriever", outcome.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_eval_does_not_introduce_fusion()
    {
        var inner = new CapturingLiveRetriever();
        var outcome = LiveRetrievalEvalRunner.RunWithProductionAbstractions(
            inner,
            new Fixed3072EmbeddingProvider(),
            cases: MiniCorpus(),
            runAzureProbes: true);

        Assert.Equal(LiveRetrievalEvalOutcome.RanStatus, outcome.Status);
        var text = outcome.Report!.ToString();
        Assert.Contains("routed lexical-or-vector", text, StringComparison.Ordinal);
        Assert.DoesNotContain("combined ranking", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fused retrieval", text, StringComparison.OrdinalIgnoreCase);
        Assert.All(inner.Kinds, k => Assert.True(k is RetrievalQueryKind.Lexical or RetrievalQueryKind.Vector));
        Assert.True(inner.MaxTopK <= 8);
        Assert.All(outcome.Report.Modes, m => Assert.True(m.AvgCosmosQueries <= 1.01));
    }

    [Fact]
    public void Optional_environment_live_run_does_not_fail_offline_suite()
    {
        var outcome = LiveRetrievalEvalRunner.TryRunFromEnvironment();
        if (outcome.IsPending)
        {
            Assert.Equal(LiveRetrievalEvalOutcome.CitationFaithfulnessNotMeasured, outcome.CitationFaithfulness);
            return;
        }

        if (outcome.Status == LiveRetrievalEvalOutcome.FailedClosedStatus)
        {
            Assert.DoesNotContain("AccountEndpoint", outcome.Notes, StringComparison.OrdinalIgnoreCase);
            return;
        }

        Assert.Equal(LiveRetrievalEvalOutcome.RanStatus, outcome.Status);
        Assert.NotNull(outcome.Report);
        Assert.Equal(LiveRetrievalEvalOutcome.CitationFaithfulnessNotMeasured, outcome.CitationFaithfulness);
        Assert.Contains(nameof(CosmosDocumentRetriever), outcome.Report!.Notes, StringComparison.Ordinal);
        Assert.Contains(nameof(AzureOpenAIEmbeddingProvider), outcome.Report.Notes, StringComparison.Ordinal);
        Assert.NotNull(outcome.Azure);
        Assert.True(outcome.Azure!.TopKCapEnforced);
        Assert.Equal(CosmosDocumentsContract.VectorDimensions, outcome.Azure.VectorContractDimensions);
    }

    private static IReadOnlyList<RetrievalEvalCase> MiniCorpus() =>
    [
        new RetrievalEvalCase(
            "L1",
            "CLAUSE-1",
            RetrievalEvalCategory.ExactIdentifier,
            ["notice|current|CLAUSE-1"],
            ExpectedDocumentCategory: "NoticePolicy",
            ExpectedVersion: "current",
            ExpectedRegion: "WEST",
            ExpectedDealerUrn: "dealer:west-1"),
        new RetrievalEvalCase(
            "S1",
            "when should a notice be held for an open dispute?",
            RetrievalEvalCategory.SemanticParaphrase,
            ["notice|current|CLAUSE-2"],
            ExpectedRegion: "WEST",
            ExpectedDealerUrn: "dealer:west-1"),
        new RetrievalEvalCase(
            "N1",
            "zzzx-no-such-policy-term",
            RetrievalEvalCategory.Negative,
            [],
            ExpectNoRelevantHit: true)
    ];

    private sealed class CapturingLiveRetriever : IDocumentRetriever
    {
        public int WriteCount { get; } = 0;
        public int? LastTopK { get; private set; }
        public int MaxTopK { get; private set; }
        public List<RetrievalQueryKind> Kinds { get; } = [];

        public Task<IReadOnlyList<EvidenceSource>> RetrieveAsync(
            DocumentRetrievalRequest request,
            CancellationToken cancellationToken)
        {
            Kinds.Add(request.Kind);
            LastTopK = request.TopK;
            MaxTopK = Math.Max(MaxTopK, request.TopK);
            return Task.FromResult<IReadOnlyList<EvidenceSource>>([]);
        }
    }

    private sealed class Fixed3072EmbeddingProvider : IEmbeddingProvider
    {
        public bool IsAvailable => true;
        public int Dimensions => CosmosDocumentsContract.VectorDimensions;
        public string ModelId => "eval-fixed-3072";

        public Task<float[]?> EmbedQueryAsync(string text, CancellationToken cancellationToken)
            => Task.FromResult<float[]?>(new float[CosmosDocumentsContract.VectorDimensions]);

        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
            IEnumerable<string> inputs,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<float[]>>(
                inputs.Select(_ => new float[CosmosDocumentsContract.VectorDimensions]).ToList());
    }
}

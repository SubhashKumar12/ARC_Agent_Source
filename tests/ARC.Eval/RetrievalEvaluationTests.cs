using ARC.Eval.Retrieval;
using ARC.Eval.Retrieval.Offline;
using Xunit.Abstractions;
using ARC.Eval.Retrieval.Live;

namespace ARC.Eval;

public sealed class RetrievalMetricsTests
{
    [Fact]
    public void RecallAtK_is_fraction_of_relevant_in_topK()
    {
        var relevant = new[] { "a", "b" };
        var ranked = new[] { "x", "a", "b", "y" };
        Assert.Equal(0.5, RetrievalMetrics.RecallAtK(relevant, ranked, 2), 3);
        Assert.Equal(1.0, RetrievalMetrics.RecallAtK(relevant, ranked, 3), 3);
    }

    [Fact]
    public void HitRateAtK_is_one_when_any_relevant_in_topK()
    {
        Assert.Equal(1.0, RetrievalMetrics.HitRateAtK(["a"], ["x", "a"], 2));
        Assert.Equal(0.0, RetrievalMetrics.HitRateAtK(["a"], ["x", "y"], 2));
    }

    [Fact]
    public void MRR_uses_first_relevant_rank()
    {
        Assert.Equal(0.5, RetrievalMetrics.MeanReciprocalRank(["a"], ["x", "a", "y"]), 3);
        Assert.Equal(0.0, RetrievalMetrics.MeanReciprocalRank(["a"], ["x", "y"]), 3);
        Assert.Equal(1.0, RetrievalMetrics.MeanReciprocalRank([], []), 3);
    }

    [Fact]
    public void Zero_result_query_with_relevant_labels_scores_zero()
    {
        Assert.Equal(0.0, RetrievalMetrics.RecallAtK(["a"], Array.Empty<string>(), 8));
        Assert.Equal(0.0, RetrievalMetrics.HitRateAtK(["a"], Array.Empty<string>(), 8));
        Assert.Equal(0.0, RetrievalMetrics.MeanReciprocalRank(["a"], Array.Empty<string>()));
    }

    [Fact]
    public void Citation_coverage_requires_source_section_version_blob()
    {
        Assert.True(RetrievalMetrics.IsCitationComplete("src", "CLAUSE-1", "current", "blob://x"));
        Assert.False(RetrievalMetrics.IsCitationComplete("src", "CLAUSE-1", "current", null));
        Assert.False(RetrievalMetrics.IsCitationComplete(null, "CLAUSE-1", "current", "blob://x"));
    }

    [Fact]
    public void Multiple_relevant_chunks_partial_recall()
    {
        var relevant = new[] { "a", "b", "c" };
        var ranked = new[] { "a", "z", "b" };
        Assert.Equal(2.0 / 3.0, RetrievalMetrics.RecallAtK(relevant, ranked, 8), 3);
    }
}

public sealed class RetrievalEvaluationTests
{
    private readonly ITestOutputHelper _output;

    public RetrievalEvaluationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Corpus_has_30_to_50_deterministic_cases()
    {
        var cases = RetrievalEvalCorpus.Create();
        Assert.InRange(cases.Count, 30, 50);
        Assert.Equal(cases.Count, cases.Select(c => c.CaseId).Distinct().Count());
        Assert.Contains(cases, c => c.Category == RetrievalEvalCategory.ExactIdentifier);
        Assert.Contains(cases, c => c.Category == RetrievalEvalCategory.SemanticParaphrase);
        Assert.Contains(cases, c => c.Category == RetrievalEvalCategory.Hybrid);
        Assert.Contains(cases, c => c.Category == RetrievalEvalCategory.Policy);
        Assert.Contains(cases, c => c.Category == RetrievalEvalCategory.Template);
        Assert.Contains(cases, c => c.Category == RetrievalEvalCategory.Supervisory);
        Assert.Contains(cases, c => c.Category == RetrievalEvalCategory.MetadataIsolation);
        Assert.Contains(cases, c => c.Category == RetrievalEvalCategory.Negative);
        Assert.All(cases.Where(c => !c.ExpectNoRelevantHit), c => Assert.NotEmpty(c.RelevantLogicalKeys));
    }

    [Fact]
    public void Synthetic_corpus_is_marked_and_includes_security_fixtures()
    {
        var docs = SyntheticKnowledgeCorpus.CreateDocuments();
        Assert.Contains(docs, d => d.status == "INACTIVE");
        Assert.Contains(docs, d => d.version == SyntheticKnowledgeCorpus.VersionLegacy);
        Assert.Contains(docs, d => d.regionScope!.Contains("WEST"));
        Assert.Contains(docs, d => d.dealerUrn == "dealer:west-1");
        Assert.All(docs, d => Assert.StartsWith("synthetic/", d.blobLocation));
    }

    [Fact]
    public void Offline_framework_validation_is_deterministic_and_secure()
    {
        var report1 = RetrievalEvalRunner.RunOfflineFrameworkValidation();
        var report2 = RetrievalEvalRunner.RunOfflineFrameworkValidation();
        _output.WriteLine(report1.ToString());

        Assert.Equal("FRAMEWORK VALIDATION ONLY", report1.MeasurementKind);
        Assert.Equal(3, report1.Modes.Count);
        Assert.All(report1.Modes, m => Assert.Equal(0, m.MetadataViolationCount));

        // Deterministic aggregates across reruns.
        for (var i = 0; i < report1.Modes.Count; i++)
        {
            Assert.Equal(report1.Modes[i].RecallAt8, report2.Modes[i].RecallAt8);
            Assert.Equal(report1.Modes[i].Mrr, report2.Modes[i].Mrr);
            Assert.Equal(report1.Modes[i].MetadataViolationCount, report2.Modes[i].MetadataViolationCount);
        }

        // Lexical exact identifiers should avoid query embeddings on average lower than vector mode.
        var lexical = report1.Modes.Single(m => m.Mode == "LexicalOnly");
        var vector = report1.Modes.Single(m => m.Mode == "VectorOnly");
        Assert.True(lexical.AvgEmbeddingCalls <= vector.AvgEmbeddingCalls);
        Assert.Equal(LiveRetrievalEvalOutcome.CitationFaithfulnessNotMeasured, report1.CitationFaithfulness);
        var hybrid = report1.Modes.Single(m => m.Mode == "Hybrid");
        Assert.All(hybrid.RouteCounts.Keys, k => Assert.True(
            k is "Lexical" or "Vector",
            $"Hybrid route diagnostic must be Lexical or Vector, not {k}"));
        Assert.True(hybrid.AvgCosmosQueries <= 1.01);
    }

    [Fact]
    public void Real_cosmos_evaluation_is_not_fabricated_when_unavailable()
    {
        var previousEnable = Environment.GetEnvironmentVariable(LiveRetrievalEvalRunner.EnableEnvironmentVariable);
        var previousCosmos = Environment.GetEnvironmentVariable("ARC_COSMOS_CONNECTION_STRING");
        try
        {
            Environment.SetEnvironmentVariable(LiveRetrievalEvalRunner.EnableEnvironmentVariable, null);
            Environment.SetEnvironmentVariable("ARC_COSMOS_CONNECTION_STRING", null);
            Assert.Null(RetrievalEvalRunner.TryRunRealCosmosEvaluation());
            var pending = LiveRetrievalEvalRunner.TryRunFromEnvironment();
            Assert.True(pending.IsPending);
            Assert.Equal(LiveRetrievalEvalOutcome.PendingStatus, pending.Status);
            Assert.Equal(LiveRetrievalEvalOutcome.CitationFaithfulnessNotMeasured, pending.CitationFaithfulness);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LiveRetrievalEvalRunner.EnableEnvironmentVariable, previousEnable);
            Environment.SetEnvironmentVariable("ARC_COSMOS_CONNECTION_STRING", previousCosmos);
        }
    }

    [Fact]
    public void Report_generation_includes_mode_table_and_categories()
    {
        var report = RetrievalEvalRunner.RunOfflineFrameworkValidation();
        var text = report.ToString();
        Assert.Contains("FRAMEWORK VALIDATION ONLY", text, StringComparison.Ordinal);
        Assert.Contains("LexicalOnly", text, StringComparison.Ordinal);
        Assert.Contains("VectorOnly", text, StringComparison.Ordinal);
        Assert.Contains("Hybrid", text, StringComparison.Ordinal);
        Assert.Contains("Category R@8", text, StringComparison.Ordinal);
        Assert.Contains("NOT Cosmos VectorDistance", text, StringComparison.Ordinal);
        Assert.Contains("CitationFaithfulness = NOT MEASURED", text, StringComparison.Ordinal);
        Assert.Contains("routed lexical-or-vector", text, StringComparison.Ordinal);
        Assert.Contains("Route counts", text, StringComparison.Ordinal);
        Assert.Contains("not fused ranking", text, StringComparison.Ordinal);
        Assert.DoesNotContain("combined ranking", text, StringComparison.OrdinalIgnoreCase);
    }
}

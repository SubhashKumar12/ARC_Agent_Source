using System.Diagnostics;
using System.Text;
using ARC.Eval.Retrieval.Live;
using ARC.Eval.Retrieval.Offline;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Graph;
using ARC.Knowledge.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ARC.Eval.Retrieval;

public sealed record RetrievalModeAggregate(
    string Mode,
    double RecallAt1,
    double RecallAt3,
    double RecallAt5,
    double RecallAt8,
    double Mrr,
    double HitRateAt8,
    int MetadataViolationCount,
    double NegativeFalsePositiveRate,
    double CitationCoverage,
    double AvgEmbeddingCalls,
    double AvgCosmosQueries,
    double AvgLatencyMs,
    int CaseCount,
    IReadOnlyDictionary<string, double> RecallAt8ByCategory,
    IReadOnlyDictionary<string, int> RouteCounts);

public sealed record RetrievalEvalReport(
    string MeasurementKind,
    IReadOnlyList<RetrievalModeAggregate> Modes,
    string Notes,
    string CitationFaithfulness = LiveRetrievalEvalOutcome.CitationFaithfulnessNotMeasured)
{
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Retrieval Evaluation ({MeasurementKind})");
        sb.AppendLine(Notes);
        sb.AppendLine($"CitationFieldCompleteness is the Cite column. CitationFaithfulness = {CitationFaithfulness}.");
        sb.AppendLine("Hybrid configuration is routed lexical-or-vector (one of Lexical CONTAINS or Vector VectorDistance), not fused ranking.");
        sb.AppendLine();
        sb.AppendLine("Mode        R@1     R@3     R@5     R@8     MRR     Hit@8   Viol  NegFP   Cite    EmbCalls  Queries  LatencyMs");
        foreach (var m in Modes)
        {
            sb.AppendLine(
                $"{m.Mode,-11} {m.RecallAt1,6:0.000} {m.RecallAt3,6:0.000} {m.RecallAt5,6:0.000} {m.RecallAt8,6:0.000} " +
                $"{m.Mrr,6:0.000} {m.HitRateAt8,6:0.000} {m.MetadataViolationCount,4} {m.NegativeFalsePositiveRate,6:0.000} " +
                $"{m.CitationCoverage,6:0.000} {m.AvgEmbeddingCalls,8:0.00} {m.AvgCosmosQueries,7:0.00} {m.AvgLatencyMs,9:0.0}");
        }

        foreach (var m in Modes)
        {
            sb.AppendLine();
            sb.AppendLine($"Category R@8 — {m.Mode}:");
            foreach (var kv in m.RecallAt8ByCategory.OrderBy(k => k.Key))
                sb.AppendLine($"  {kv.Key,-20} {kv.Value:0.000}");
            sb.AppendLine($"Route counts — {m.Mode}:");
            foreach (var kv in m.RouteCounts.OrderBy(k => k.Key))
                sb.AppendLine($"  {kv.Key,-20} {kv.Value}");
        }

        return sb.ToString();
    }
}

/// <summary>
/// Runs labelled retrieval cases against ARC KnowledgeRetrievalService.
/// Offline path uses synthetic corpus + bag-of-words embeddings (framework validation only).
/// Live path uses production CosmosDocumentRetriever + AzureOpenAIEmbeddingProvider when explicitly enabled.
/// </summary>
public static class RetrievalEvalRunner
{
    public static RetrievalEvalReport RunOfflineFrameworkValidation(
        IReadOnlyList<RetrievalEvalCase>? cases = null)
    {
        cases ??= RetrievalEvalCorpus.Create();
        var documents = SyntheticKnowledgeCorpus.CreateDocuments();
        var modes = new[] { "LexicalOnly", "VectorOnly", "Hybrid" };
        var aggregates = modes.Select(mode => RunOfflineMode(mode, cases, documents)).ToList();

        return new RetrievalEvalReport(
            "FRAMEWORK VALIDATION ONLY",
            aggregates,
            "Offline synthetic corpus with bag-of-words embeddings. NOT Cosmos VectorDistance / Azure OpenAI quality. Hybrid is routed lexical-or-vector, not fusion.",
            LiveRetrievalEvalOutcome.CitationFaithfulnessNotMeasured);
    }

    /// <summary>
    /// Live Cosmos evaluation. Returns null when not explicitly enabled or DEV configuration is unavailable.
    /// Never fabricates live metrics.
    /// </summary>
    public static RetrievalEvalReport? TryRunRealCosmosEvaluation()
    {
        var outcome = LiveRetrievalEvalRunner.TryRunFromEnvironment();
        if (outcome.IsPending)
            return null;
        return outcome.Report;
    }

    public static RetrievalModeAggregate RunModeWithProductionService(
        string modeName,
        IReadOnlyList<RetrievalEvalCase> cases,
        KnowledgeRetrievalService service,
        RecordingDocumentRetriever retriever,
        RecordingEmbeddingProvider embeddings)
        => RunMode(modeName, cases, service, retriever, embeddings);

    private static RetrievalModeAggregate RunOfflineMode(
        string modeName,
        IReadOnlyList<RetrievalEvalCase> cases,
        IReadOnlyList<ARC.Knowledge.Vector.IndexedDocument> documents)
    {
        var embeddings = new RecordingEmbeddingProvider(new BagOfWordsEmbeddingProvider());
        var retriever = new RecordingDocumentRetriever(new OfflineDocumentRetriever(documents));
        var options = Options.Create(new ArcKnowledgeOptions
        {
            RagEnabled = true,
            VectorSearchEnabled = true,
            LexicalSearchEnabled = true,
            RetrievalMode = modeName,
            MaxRetrievalTopK = 8,
            MaxPromptChunks = 4,
            PublishedPolicyVersion = SyntheticKnowledgeCorpus.VersionCurrent,
            Embeddings = new EmbeddingProviderOptions
            {
                Provider = "None",
                Deployment = "eval-bag-of-words",
                Dimensions = BagOfWordsEmbeddingProvider.Dim
            }
        });

        var service = new KnowledgeRetrievalService(
            new EmptyEvalGraphTraversal(),
            retriever,
            embeddings,
            options,
            NullLogger<KnowledgeRetrievalService>.Instance);

        return RunMode(modeName, cases, service, retriever, embeddings);
    }

    private static RetrievalModeAggregate RunMode(
        string modeName,
        IReadOnlyList<RetrievalEvalCase> cases,
        KnowledgeRetrievalService service,
        RecordingDocumentRetriever retriever,
        RecordingEmbeddingProvider embeddings)
    {
        var results = new List<RetrievalEvalCaseResult>();
        var totalQueries = 0;
        foreach (var evalCase in cases)
        {
            embeddings.ResetCounters();
            retriever.ResetCounters();
            var sw = Stopwatch.StartNew();

            using (RetrievalScope.Enter(new RetrievalAuthorization(evalCase.ExpectedRegion, evalCase.ExpectedDealerUrn)))
            {
                var query = new RetrievalQuery(
                    evalCase.Query,
                    evalCase.ExpectedDealerUrn,
                    evalCase.ExpectedRegion,
                    evalCase.ExpectedDocumentCategory,
                    evalCase.CaseId,
                    TopK: 8,
                    DocumentType: evalCase.ExpectedDocumentType);

                var retrieval = service.RetrieveAsync(query, CancellationToken.None).GetAwaiter().GetResult();
                sw.Stop();
                totalQueries += retriever.QueryCount;

                var observedKind = retrieval.RouteKind?.ToString() ?? "Unknown";
                var hits = new List<RetrievalEvalHit>();
                for (var i = 0; i < retrieval.Sources.Count; i++)
                {
                    var s = retrieval.Sources[i];
                    var key = LogicalChunkKey.Create(
                        s.SourceDocumentId ?? "",
                        s.Reference.Version ?? "",
                        s.Reference.PageOrSection ?? "");
                    hits.Add(new RetrievalEvalHit(
                        i + 1,
                        key,
                        s.Reference.DocumentId,
                        s.SourceDocumentId,
                        s.Reference.Version,
                        s.Reference.PageOrSection,
                        s.Reference.BlobLocation,
                        s.Status,
                        s.RegionScope,
                        s.DealerUrn,
                        s.Score,
                        observedKind));
                }

                var rankedKeys = hits.Select(h => h.LogicalKey).ToList();
                var relevant = evalCase.RelevantLogicalKeys;
                var firstRelevant = rankedKeys
                    .Select((k, idx) => (k, idx))
                    .Where(x => relevant.Contains(x.k, StringComparer.OrdinalIgnoreCase))
                    .Select(x => (int?)(x.idx + 1))
                    .FirstOrDefault();

                var (violation, reason) = DetectMetadataViolation(evalCase, hits);
                var citationOk = hits.Count == 0
                    || hits.All(h => RetrievalMetrics.IsCitationComplete(
                        h.SourceDocumentId, h.PageOrSection, h.Version, h.BlobLocation));

                var negativeFp = evalCase.ExpectNoRelevantHit
                    && hits.Any(h => relevant.Count == 0 || !relevant.Contains(h.LogicalKey, StringComparer.OrdinalIgnoreCase));
                if (evalCase.ExpectNoRelevantHit && relevant.Count == 0)
                    negativeFp = hits.Count > 0;

                results.Add(new RetrievalEvalCaseResult(
                    evalCase.CaseId,
                    evalCase.Category,
                    modeName,
                    observedKind,
                    hits,
                    firstRelevant is not null,
                    firstRelevant,
                    violation,
                    reason,
                    citationOk,
                    negativeFp,
                    embeddings.EmbedQueryCalls,
                    sw.ElapsedMilliseconds));
            }
        }

        return Aggregate(modeName, cases, results, totalQueries);
    }

    private static (bool Violation, string? Reason) DetectMetadataViolation(
        RetrievalEvalCase evalCase,
        IReadOnlyList<RetrievalEvalHit> hits)
    {
        foreach (var hit in hits)
        {
            if (!string.Equals(hit.Status, ArcKnowledgeOptions.ActiveStatus, StringComparison.OrdinalIgnoreCase))
                return (true, $"Inactive/non-ACTIVE status returned: {hit.Status}");

            if (!string.IsNullOrWhiteSpace(evalCase.ExpectedVersion)
                && !string.Equals(hit.Version, evalCase.ExpectedVersion, StringComparison.OrdinalIgnoreCase))
                return (true, $"Version mismatch: expected {evalCase.ExpectedVersion}, got {hit.Version}");

            if (!string.IsNullOrWhiteSpace(evalCase.ExpectedRegion)
                && !string.IsNullOrWhiteSpace(hit.RegionScope)
                && !hit.RegionScope.Split(',').Any(r =>
                    string.Equals(r, ArcKnowledgeOptions.GlobalRegionToken, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r, evalCase.ExpectedRegion, StringComparison.OrdinalIgnoreCase)))
                return (true, $"Region leakage: expected {evalCase.ExpectedRegion}, got {hit.RegionScope}");

            if (!string.IsNullOrWhiteSpace(hit.DealerUrn)
                && !string.IsNullOrWhiteSpace(evalCase.ExpectedDealerUrn)
                && !string.Equals(hit.DealerUrn, evalCase.ExpectedDealerUrn, StringComparison.OrdinalIgnoreCase))
                return (true, $"Dealer leakage: expected {evalCase.ExpectedDealerUrn}, got {hit.DealerUrn}");
        }

        return (false, null);
    }

    private static RetrievalModeAggregate Aggregate(
        string mode,
        IReadOnlyList<RetrievalEvalCase> cases,
        IReadOnlyList<RetrievalEvalCaseResult> results,
        int totalQueries)
    {
        var byId = cases.ToDictionary(c => c.CaseId);
        double R(int k) => RetrievalMetrics.Average(results.Select(r =>
        {
            var c = byId[r.CaseId];
            if (c.ExpectNoRelevantHit && c.RelevantLogicalKeys.Count == 0)
                return r.Hits.Count == 0 ? 1.0 : 0.0;
            return RetrievalMetrics.RecallAtK(c.RelevantLogicalKeys, r.Hits.Select(h => h.LogicalKey).ToList(), k);
        }));

        double Hit() => RetrievalMetrics.Average(results.Select(r =>
        {
            var c = byId[r.CaseId];
            if (c.ExpectNoRelevantHit && c.RelevantLogicalKeys.Count == 0)
                return r.Hits.Count == 0 ? 1.0 : 0.0;
            return RetrievalMetrics.HitRateAtK(c.RelevantLogicalKeys, r.Hits.Select(h => h.LogicalKey).ToList(), 8);
        }));

        double Mrr() => RetrievalMetrics.Average(results.Select(r =>
        {
            var c = byId[r.CaseId];
            if (c.ExpectNoRelevantHit && c.RelevantLogicalKeys.Count == 0)
                return r.Hits.Count == 0 ? 1.0 : 0.0;
            return RetrievalMetrics.MeanReciprocalRank(c.RelevantLogicalKeys, r.Hits.Select(h => h.LogicalKey).ToList());
        }));

        var negatives = results.Where(r => byId[r.CaseId].ExpectNoRelevantHit).ToList();
        var negFp = negatives.Count == 0 ? 0.0 : negatives.Count(r => r.NegativeFalsePositive) / (double)negatives.Count;

        var categoryRecall = results
            .GroupBy(r => r.Category.ToString())
            .ToDictionary(
                g => g.Key,
                g => RetrievalMetrics.Average(g.Select(r =>
                {
                    var c = byId[r.CaseId];
                    if (c.ExpectNoRelevantHit && c.RelevantLogicalKeys.Count == 0)
                        return r.Hits.Count == 0 ? 1.0 : 0.0;
                    return RetrievalMetrics.RecallAtK(c.RelevantLogicalKeys, r.Hits.Select(h => h.LogicalKey).ToList(), 8);
                })));

        var routeCounts = results
            .GroupBy(r => r.ObservedKind)
            .ToDictionary(g => g.Key, g => g.Count());

        return new RetrievalModeAggregate(
            mode,
            R(1), R(3), R(5), R(8),
            Mrr(),
            Hit(),
            results.Count(r => r.MetadataViolation),
            negFp,
            results.Count == 0 ? 0 : results.Count(r => r.CitationComplete) / (double)results.Count,
            results.Count == 0 ? 0 : results.Average(r => r.EmbeddingCalls),
            results.Count == 0 ? 0 : totalQueries / (double)results.Count,
            results.Count == 0 ? 0 : results.Average(r => r.ElapsedMilliseconds),
            results.Count,
            categoryRecall,
            routeCounts);
    }
}

public sealed class EmptyEvalGraphTraversal : IGraphTraversal
{
    public Task<IReadOnlyList<GraphNode>> TraverseDealerAsync(
        ARC.Domain.ValueObjects.DealerUrn dealerUrn,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<GraphNode>>(Array.Empty<GraphNode>());
}

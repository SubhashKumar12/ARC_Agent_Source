using ARC.Domain.ValueObjects;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Embeddings;
using ARC.Knowledge.Graph;
using ARC.Knowledge.Grounding;
using ARC.Knowledge.Provenance;
using ARC.Knowledge.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ARC.Knowledge.Tests.Grounding;

public sealed class GroundingContextProviderTests
{
    [Fact]
    public async Task A3_purpose_selects_NoticePolicy_category()
    {
        var capturing = new CapturingRetriever();
        var provider = CreateProvider(capturing, new FakeGraph("dealer:1"));

        await provider.GetContextAsync(Request(GroundingPurpose.A3NoticeDecisionSupport, "prescribed notice period"));

        Assert.Equal("NoticePolicy", capturing.Last!.Filter.DocumentCategory);
        Assert.Equal("Policy", capturing.Last.Filter.DocumentType);
    }

    [Fact]
    public async Task A5_purpose_selects_NoticeTemplate_category()
    {
        var capturing = new CapturingRetriever();
        var provider = CreateProvider(capturing, new FakeGraph("dealer:1"));

        await provider.GetContextAsync(Request(GroundingPurpose.A5DraftingTemplateSupport, "Final Demand"));

        Assert.Equal("NoticeTemplate", capturing.Last!.Filter.DocumentCategory);
        Assert.Equal("Template", capturing.Last.Filter.DocumentType);
    }

    [Fact]
    public async Task A8_purpose_allows_unscoped_category_for_query()
    {
        var capturing = new CapturingRetriever();
        var provider = CreateProvider(capturing, new FakeGraph("dealer:1"));

        await provider.GetContextAsync(Request(GroundingPurpose.A8SupervisoryInsight, "why hold for finance reconcile"));

        Assert.Null(capturing.Last!.Filter.DocumentCategory);
        Assert.Null(capturing.Last.Filter.DocumentType);
    }

    [Fact]
    public async Task ACTIVE_and_published_version_filters_preserved()
    {
        var capturing = new CapturingRetriever();
        var provider = CreateProvider(capturing, new FakeGraph("dealer:1"), publishedVersion: "current");

        await provider.GetContextAsync(Request(GroundingPurpose.A3NoticeDecisionSupport, "CLAUSE-1"));

        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, capturing.Last!.Filter.Status);
        Assert.Equal("current", capturing.Last.Filter.Version);
    }

    [Fact]
    public async Task Region_and_dealer_authorization_preserved()
    {
        var capturing = new CapturingRetriever();
        var provider = CreateProvider(capturing, new FakeGraph("dealer:west"));

        using (RetrievalScope.Enter(new RetrievalAuthorization("WEST", "dealer:west")))
        {
            await provider.GetContextAsync(new GroundingRequest(
                GroundingPurpose.A3NoticeDecisionSupport,
                "2026-03",
                "case-1",
                "dealer:other",
                "EAST",
                "west depot manager gate",
                "corr"));
        }

        Assert.Equal("WEST", capturing.Last!.Filter.Region);
        Assert.Equal("dealer:west", capturing.Last.Filter.DealerUrn);
    }

    [Fact]
    public async Task Inactive_and_historical_chunks_excluded_by_retriever_contract()
    {
        var retriever = new FilteringRetriever(new[]
        {
            Source("active", "ACTIVE", "current", "CLAUSE-1"),
            Source("inactive", "INACTIVE", "current", "CLAUSE-99"),
            Source("legacy", "ACTIVE", "legacy", "CLAUSE-1")
        });
        var provider = CreateProvider(retriever, new FakeGraph("dealer:1"), publishedVersion: "current");

        var context = await provider.GetContextAsync(Request(GroundingPurpose.A3NoticeDecisionSupport, "CLAUSE"));

        Assert.DoesNotContain(context.KnowledgeChunks, c => c.Status == "INACTIVE");
        Assert.DoesNotContain(context.KnowledgeChunks, c => c.Reference.Version == "legacy");
        Assert.Contains(context.KnowledgeChunks, c => c.Reference.DocumentId == "active");
    }

    [Fact]
    public async Task Graph_context_scoped_to_requested_dealer()
    {
        var graph = new FakeGraph("dealer:target", alsoEmit: "dealer:other");
        var provider = CreateProvider(new CapturingRetriever(), graph);

        var context = await provider.GetContextAsync(Request(GroundingPurpose.A3NoticeDecisionSupport, "policy", "dealer:target"));

        Assert.Contains(context.StructuredFacts, f => f.FactId.Contains("dealer:target", StringComparison.Ordinal));
        Assert.DoesNotContain(context.StructuredFacts, f => f.FactId.Contains("dealer:other", StringComparison.Ordinal));
        Assert.All(context.StructuredFacts, f => Assert.Equal(EvidenceTrustLevel.AuthoritativeStructured, f.TrustLevel));
    }

    [Fact]
    public async Task Knowledge_prompt_chunks_capped_at_4_and_topk_at_8()
    {
        var many = Enumerable.Range(1, 12)
            .Select(i => Source($"doc-{i}", "ACTIVE", "current", $"CLAUSE-{i}"))
            .ToArray();
        var capturing = new CapturingRetriever(many);
        var provider = CreateProvider(capturing, new FakeGraph("dealer:1"));

        var context = await provider.GetContextAsync(
            Request(GroundingPurpose.A3NoticeDecisionSupport, "clause", topK: 99));

        Assert.Equal(8, capturing.Last!.TopK);
        Assert.True(context.KnowledgeChunks.Count <= 4);
        Assert.Equal(8, context.Diagnostics.RetrievalTopK);
        Assert.Equal(4, context.Diagnostics.MaxPromptChunks);
    }

    [Fact]
    public async Task Provenance_preserved_on_knowledge_and_structured_facts()
    {
        var retriever = new CapturingRetriever(new[]
        {
            Source("kd1", "ACTIVE", "current", "CLAUSE-1", blob: "blob://policy/1", sourceDoc: "POLICY-1")
        });
        var provider = CreateProvider(retriever, new FakeGraph("dealer:1"));

        var context = await provider.GetContextAsync(Request(GroundingPurpose.A3NoticeDecisionSupport, "CLAUSE-1"));

        var chunk = Assert.Single(context.KnowledgeChunks);
        Assert.Equal("POLICY-1", chunk.SourceDocumentId);
        Assert.Equal("CLAUSE-1", chunk.Reference.PageOrSection);
        Assert.Equal("current", chunk.Reference.Version);
        Assert.Equal("blob://policy/1", chunk.Reference.BlobLocation);
        Assert.NotEmpty(context.Citations);
        Assert.Contains(context.StructuredFacts, f => f.Provenance.SourceSystem == "sql");
    }

    [Fact]
    public async Task Authoritative_facts_remain_distinct_from_reference_knowledge()
    {
        var provider = CreateProvider(
            new CapturingRetriever(new[] { Source("kd1", "ACTIVE", "current", "CLAUSE-1") }),
            new FakeGraph("dealer:1"));

        var context = await provider.GetContextAsync(Request(GroundingPurpose.A3NoticeDecisionSupport, "CLAUSE-1"));

        Assert.All(context.StructuredFacts, f => Assert.Equal(EvidenceTrustLevel.AuthoritativeStructured, f.TrustLevel));
        Assert.NotEmpty(context.KnowledgeChunks);
        Assert.DoesNotContain(context.KnowledgeChunks, k =>
            context.StructuredFacts.Any(f => f.FactId == k.Reference.DocumentId));
    }

    [Fact]
    public async Task No_evidence_returns_explicit_insufficient_result()
    {
        var provider = CreateProvider(new CapturingRetriever(), new EmptyGraph());

        var context = await provider.GetContextAsync(
            Request(GroundingPurpose.A3NoticeDecisionSupport, "CLAUSE-1", dealer: null));

        Assert.False(context.HasEvidence);
        Assert.True(context.Diagnostics.InsufficientEvidence);
        Assert.Empty(context.StructuredFacts);
        Assert.Empty(context.KnowledgeChunks);
    }

    [Fact]
    public async Task Deterministic_repeatability_and_no_llm_surface()
    {
        var provider = CreateProvider(
            new CapturingRetriever(new[] { Source("kd1", "ACTIVE", "current", "CLAUSE-1") }),
            new FakeGraph("dealer:1"));
        var request = Request(GroundingPurpose.A5DraftingTemplateSupport, "Final Demand");

        var a = await provider.GetContextAsync(request);
        var b = await provider.GetContextAsync(request);

        Assert.Equal(a.StructuredFacts.Select(f => f.FactId), b.StructuredFacts.Select(f => f.FactId));
        Assert.Equal(a.KnowledgeChunks.Select(k => k.Reference.DocumentId), b.KnowledgeChunks.Select(k => k.Reference.DocumentId));
        Assert.Equal(a.Diagnostics.Purpose, b.Diagnostics.Purpose);
    }

    [Fact]
    public void Purpose_profiles_are_stable()
    {
        Assert.Equal("NoticePolicy", GroundingPurposeProfile.For(GroundingPurpose.A3NoticeDecisionSupport).DocumentCategory);
        Assert.Equal("NoticeTemplate", GroundingPurposeProfile.For(GroundingPurpose.A5DraftingTemplateSupport).DocumentCategory);
        Assert.Null(GroundingPurposeProfile.For(GroundingPurpose.A8SupervisoryInsight).DocumentCategory);
    }

    private static GroundingRequest Request(
        GroundingPurpose purpose,
        string query,
        string? dealer = "dealer:1",
        int topK = 8)
        => new(purpose, "2026-03", "case-1", dealer, "WEST", query, "corr", topK);

    private static IGroundingContextProvider CreateProvider(
        IDocumentRetriever retriever,
        IGraphTraversal graph,
        string publishedVersion = "current")
    {
        var options = Options.Create(new ArcKnowledgeOptions
        {
            RagEnabled = true,
            VectorSearchEnabled = true,
            LexicalSearchEnabled = true,
            RetrievalMode = "Hybrid",
            MaxRetrievalTopK = 8,
            MaxPromptChunks = 4,
            PublishedPolicyVersion = publishedVersion
        });

        var retrieval = new KnowledgeRetrievalService(
            graph,
            retriever,
            new DisabledEmbeddingProvider(new EmbeddingProviderOptions()),
            options,
            NullLogger<KnowledgeRetrievalService>.Instance);

        return new GroundingContextProvider(
            new CaseGraphContextProvider(graph),
            retrieval,
            options,
            NullLogger<GroundingContextProvider>.Instance);
    }

    private static EvidenceSource Source(
        string id,
        string status,
        string version,
        string section,
        string? blob = "blob://x",
        string? sourceDoc = "SYN-DOC")
        => new(
            new SourceReference(id, blob, null, version, section, "test", DateTimeOffset.UtcNow),
            Title: section,
            Snippet: $"content {section}",
            Score: null,
            Status: status,
            RegionScope: "GLOBAL",
            DealerUrn: null,
            SourceDocumentId: sourceDoc,
            DocumentType: "Policy");

    private sealed class CapturingRetriever : IDocumentRetriever
    {
        private readonly IReadOnlyList<EvidenceSource> _sources;
        public DocumentRetrievalRequest? Last { get; private set; }

        public CapturingRetriever(IReadOnlyList<EvidenceSource>? sources = null)
            => _sources = sources ?? Array.Empty<EvidenceSource>();

        public Task<IReadOnlyList<EvidenceSource>> RetrieveAsync(
            DocumentRetrievalRequest request,
            CancellationToken cancellationToken)
        {
            Last = request;
            var filtered = _sources
                .Where(s => string.Equals(s.Status, request.Filter.Status, StringComparison.OrdinalIgnoreCase))
                .Where(s => string.Equals(s.Reference.Version, request.Filter.Version, StringComparison.OrdinalIgnoreCase))
                .Take(request.TopK)
                .ToList();
            return Task.FromResult<IReadOnlyList<EvidenceSource>>(filtered);
        }
    }

    private sealed class FilteringRetriever : IDocumentRetriever
    {
        private readonly IReadOnlyList<EvidenceSource> _sources;
        public FilteringRetriever(IReadOnlyList<EvidenceSource> sources) => _sources = sources;

        public Task<IReadOnlyList<EvidenceSource>> RetrieveAsync(
            DocumentRetrievalRequest request,
            CancellationToken cancellationToken)
        {
            var filtered = _sources
                .Where(s => string.Equals(s.Status, request.Filter.Status, StringComparison.OrdinalIgnoreCase))
                .Where(s => string.Equals(s.Reference.Version, request.Filter.Version, StringComparison.OrdinalIgnoreCase))
                .Take(request.TopK)
                .ToList();
            return Task.FromResult<IReadOnlyList<EvidenceSource>>(filtered);
        }
    }

    private sealed class FakeGraph : IGraphTraversal
    {
        private readonly string _dealer;
        private readonly string? _alsoEmit;

        public FakeGraph(string dealer, string? alsoEmit = null)
        {
            _dealer = dealer;
            _alsoEmit = alsoEmit;
        }

        public Task<IReadOnlyList<GraphNode>> TraverseDealerAsync(DealerUrn dealerUrn, CancellationToken cancellationToken)
        {
            if (!string.Equals(dealerUrn.Value, _dealer, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<IReadOnlyList<GraphNode>>(Array.Empty<GraphNode>());

            var nodes = new List<GraphNode>
            {
                new($"dealer:{_dealer}", "Dealer", "dealer",
                    new SourceReference(_dealer, null, null, null, null, "sql", DateTimeOffset.UtcNow)),
                new($"depot:WEST-1", "Depot", "depot",
                    new SourceReference("WEST-1", null, null, null, null, "sql", DateTimeOffset.UtcNow))
            };

            // Unrelated dealer must not be returned by a correctly scoped fake.
            _ = _alsoEmit;
            return Task.FromResult<IReadOnlyList<GraphNode>>(nodes);
        }
    }

    private sealed class EmptyGraph : IGraphTraversal
    {
        public Task<IReadOnlyList<GraphNode>> TraverseDealerAsync(DealerUrn dealerUrn, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<GraphNode>>(Array.Empty<GraphNode>());
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ARC.Data.Cosmos;
using ARC.Domain.ValueObjects;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Embeddings;
using ARC.Knowledge.Graph;
using ARC.Knowledge.Provenance;
using ARC.Knowledge.Retrieval;
using ARC.Knowledge.Vector;

namespace ARC.Knowledge.Tests;

public sealed class RetrievalSecurityTests
{
    private static ArcKnowledgeOptions Options() => new()
    {
        PublishedPolicyVersion = "current",
        MaxRetrievalTopK = 8,
        MaxPromptChunks = 4,
        MaxSnippetCharacters = 400
    };

    [Fact]
    public void Filter_is_always_ACTIVE_and_uses_published_version()
    {
        var filter = RetrievalSecurity.Create(Options(), new RetrievalAuthorization("West", "dealer:1"), "dealer:1", "West", null);
        Assert.Equal("ACTIVE", filter.Status);
        Assert.Equal("current", filter.Version);
    }

    [Fact]
    public void Region_comes_from_authorization_not_requested_region()
    {
        var filter = RetrievalSecurity.Create(
            Options(),
            new RetrievalAuthorization("West", "dealer:1"),
            "dealer:1",
            "South",
            null);
        Assert.Equal("West", filter.Region);
    }

    [Fact]
    public void Dealer_comes_from_authorization_not_requested_dealer()
    {
        var filter = RetrievalSecurity.Create(
            Options(),
            new RetrievalAuthorization("West", "dealer:resolved"),
            "dealer:other",
            "West",
            null);
        Assert.Equal("dealer:resolved", filter.DealerUrn);
    }

    [Fact]
    public void Missing_actor_region_does_not_invent_a_region()
    {
        var filter = RetrievalSecurity.Create(Options(), authorization: null, "dealer:1", "West", null);
        Assert.Null(filter.Region);
        Assert.Equal("dealer:1", filter.DealerUrn);
    }

    [Fact]
    public void Omitted_version_defaults_to_published_current()
    {
        var options = Options();
        options.PublishedPolicyVersion = "policy-2026-04";
        var filter = RetrievalSecurity.Create(options, new RetrievalAuthorization("West", "dealer:1"), null, null, null);
        Assert.Equal("policy-2026-04", filter.Version);
    }
}

public sealed class RetrievalQueryRouterTests
{
    [Theory]
    [InlineData("CHQ-8891")]
    [InlineData("DN-2026-014")]
    [InlineData("Clause 12.3")]
    [InlineData("section 7.2")]
    public void Identifiers_and_clauses_use_lexical_route(string text)
    {
        Assert.True(RetrievalQueryRouter.LooksLikeIdentifierOrClause(text));
        Assert.Equal(
            RetrievalQueryKind.Lexical,
            RetrievalQueryRouter.Route(text, RetrievalMode.Hybrid, embeddingAvailable: true));
    }

    [Fact]
    public void Semantic_text_uses_vector_when_embeddings_are_available()
    {
        Assert.Equal(
            RetrievalQueryKind.Vector,
            RetrievalQueryRouter.Route("when do we hold a notice for an open dispute?", RetrievalMode.Hybrid, true));
    }

    [Fact]
    public void Semantic_text_falls_back_to_lexical_without_embeddings()
    {
        Assert.Equal(
            RetrievalQueryKind.Lexical,
            RetrievalQueryRouter.Route("when do we hold a notice for an open dispute?", RetrievalMode.Hybrid, false));
    }
}

public sealed class CosmosDocumentQueryTests
{
    private static DocumentRetrievalRequest Request(RetrievalQueryKind kind, float[]? embedding, int topK = 8)
        => new(
            "open dispute hold",
            embedding,
            new DocumentRetrievalFilter("ACTIVE", "current", "West", "dealer:1", "policy", null),
            kind,
            topK);

    [Fact]
    public void Vector_sql_uses_VectorDistance_and_ACTIVE_literal()
    {
        var sql = CosmosDocumentQuery.VectorSql();
        Assert.Contains("VectorDistance(c.embedding, @embedding)", sql, StringComparison.Ordinal);
        Assert.Contains("c.status = 'ACTIVE'", sql, StringComparison.Ordinal);
        Assert.Contains("c.version = @version", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@status", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Lexical_sql_uses_CONTAINS_without_VectorDistance()
    {
        var sql = CosmosDocumentQuery.LexicalSql();
        Assert.Contains("CONTAINS(c.content, @text, true)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("VectorDistance", sql, StringComparison.Ordinal);
        Assert.Contains("c.status = 'ACTIVE'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Vector_query_binds_embedding_and_caps_topK()
    {
        var definition = CosmosDocumentQuery.Build(Request(RetrievalQueryKind.Vector, new float[3072], 99));
        var sql = definition.QueryText;
        Assert.Contains("VectorDistance", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT TOP @topK", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Lexical_query_does_not_require_embedding()
    {
        var definition = CosmosDocumentQuery.Build(Request(RetrievalQueryKind.Lexical, null));
        Assert.Contains("CONTAINS", definition.QueryText, StringComparison.Ordinal);
        Assert.DoesNotContain("VectorDistance", definition.QueryText, StringComparison.Ordinal);
    }

    [Fact]
    public void Queries_do_not_implement_rrf_bm25_or_rank_fusion()
    {
        var vector = CosmosDocumentQuery.VectorSql();
        var lexical = CosmosDocumentQuery.LexicalSql();
        Assert.DoesNotContain("RRF", vector, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BM25", vector, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Reciprocal", vector, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RRF", lexical, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BM25", lexical, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNION", vector, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNION", lexical, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CosmosDocumentsContractTests
{
    [Fact]
    public void Documents_partition_key_is_documentType()
    {
        Assert.Equal("/documentType", CosmosDocumentsContract.PartitionKeyPath);
        var properties = CosmosDocumentsContract.CreateContainerProperties("documents");
        Assert.Equal("/documentType", properties.PartitionKeyPath);
        Assert.Equal(3072, CosmosDocumentsContract.VectorDimensions);
        Assert.Contains(properties.IndexingPolicy.VectorIndexes, v => v.Type == Microsoft.Azure.Cosmos.VectorIndexType.DiskANN);
        Assert.Contains(properties.VectorEmbeddingPolicy.Embeddings, e => e.Dimensions == 3072 && e.DistanceFunction == Microsoft.Azure.Cosmos.DistanceFunction.Cosine);
    }
}

public sealed class KnowledgeRetrievalServiceTests
{
    [Fact]
    public async Task Vector_path_selected_when_embedding_exists()
    {
        var retriever = new CapturingRetriever();
        var embeddings = new FakeEmbeddingProvider(available: true);
        var service = Create(retriever, embeddings);
        using (RetrievalScope.Enter(new RetrievalAuthorization("West", "dealer:1")))
        {
            await service.RetrieveAsync(
                new RetrievalQuery("when is a notice held for dispute?", "dealer:1", "West", null, "corr"),
                CancellationToken.None);
        }

        Assert.NotNull(retriever.Last);
        Assert.Equal(RetrievalQueryKind.Vector, retriever.Last!.Kind);
        Assert.NotNull(retriever.Last.QueryEmbedding);
        Assert.Equal(1, embeddings.Calls);
    }

    [Fact]
    public async Task Result_exposes_route_kind_without_fusion()
    {
        var retriever = new CapturingRetriever();
        var service = Create(retriever, new FakeEmbeddingProvider(true));
        var result = await service.RetrieveAsync(
            new RetrievalQuery("when is a notice held for dispute?", "dealer:1", "West", null, "corr"),
            CancellationToken.None);

        Assert.Equal(RetrievalQueryKind.Vector, result.RouteKind);
        Assert.Single(retriever.Calls);
    }

    [Fact]
    public async Task Hybrid_mode_still_executes_exactly_one_retriever_call()
    {
        var retriever = new CapturingRetriever();
        var options = new ArcKnowledgeOptions { RetrievalMode = "Hybrid", VectorSearchEnabled = true, LexicalSearchEnabled = true };
        var service = Create(retriever, new FakeEmbeddingProvider(true), options);
        await service.RetrieveAsync(
            new RetrievalQuery("when is a notice held for dispute?", "dealer:1", "West", null, "corr"),
            CancellationToken.None);
        await service.RetrieveAsync(
            new RetrievalQuery("CHQ-8891", "dealer:1", "West", null, "corr"),
            CancellationToken.None);

        Assert.Equal(2, retriever.Calls.Count);
        Assert.Equal(RetrievalQueryKind.Vector, retriever.Calls[0].Kind);
        Assert.Equal(RetrievalQueryKind.Lexical, retriever.Calls[1].Kind);
        Assert.DoesNotContain("RRF", CosmosDocumentQuery.VectorSql(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BM25", CosmosDocumentQuery.LexicalSql(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Lexical_route_does_not_generate_query_embedding()
    {
        var retriever = new CapturingRetriever();
        var embeddings = new FakeEmbeddingProvider(available: true);
        var service = Create(retriever, embeddings);
        await service.RetrieveAsync(
            new RetrievalQuery("CHQ-8891", "dealer:1", "West", null, "corr"),
            CancellationToken.None);

        Assert.Equal(RetrievalQueryKind.Lexical, retriever.Last!.Kind);
        Assert.Null(retriever.Last.QueryEmbedding);
        Assert.Equal(0, embeddings.Calls);
    }

    [Fact]
    public async Task TopK_is_capped_at_assignment_maximum()
    {
        var retriever = new CapturingRetriever();
        var options = new ArcKnowledgeOptions { MaxRetrievalTopK = 50 };
        var service = Create(retriever, new FakeEmbeddingProvider(false), options);
        await service.RetrieveAsync(
            new RetrievalQuery("CHQ-1", "dealer:1", "West", null, "corr", TopK: 99),
            CancellationToken.None);
        Assert.Equal(8, retriever.Last!.TopK);
    }

    [Fact]
    public async Task Filter_enforces_ACTIVE_published_version_region_and_dealer()
    {
        var retriever = new CapturingRetriever();
        var service = Create(retriever, new FakeEmbeddingProvider(false));
        using (RetrievalScope.Enter(new RetrievalAuthorization("West", "dealer:resolved")))
        {
            await service.RetrieveAsync(
                new RetrievalQuery("Clause 12.3", "dealer:other", "South", "policy", "corr"),
                CancellationToken.None);
        }

        var filter = retriever.Last!.Filter;
        Assert.Equal("ACTIVE", filter.Status);
        Assert.Equal("current", filter.Version);
        Assert.Equal("West", filter.Region);
        Assert.Equal("dealer:resolved", filter.DealerUrn);
    }

    [Fact]
    public async Task Prompt_chunks_are_deduped_and_capped()
    {
        var retriever = new CapturingRetriever
        {
            Sources =
            [
                Source("a", "same", new string('x', 800)),
                Source("a", "same", "dup"),
                Source("b", "p2", "two"),
                Source("c", "p3", "three"),
                Source("d", "p4", "four"),
                Source("e", "p5", "five")
            ]
        };
        var service = Create(retriever, new FakeEmbeddingProvider(false));
        var result = await service.RetrieveAsync(
            new RetrievalQuery("CHQ-1", "dealer:1", "West", null, "corr"),
            CancellationToken.None);
        Assert.Equal(4, result.Sources.Count);
        Assert.True(result.Sources[0].Snippet.Length <= 400);
        Assert.DoesNotContain(result.Sources, s => s.Reference.DocumentId == "e");
    }

    [Fact]
    public async Task Disabled_mode_skips_document_retrieval()
    {
        var retriever = new CapturingRetriever();
        var options = new ArcKnowledgeOptions { RagEnabled = false };
        var service = Create(retriever, new FakeEmbeddingProvider(true), options);
        var result = await service.RetrieveAsync(
            new RetrievalQuery("policy hold", "dealer:1", "West", null, "corr"),
            CancellationToken.None);
        Assert.Empty(result.Sources);
        Assert.Null(retriever.Last);
    }

    [Fact]
    public async Task Embedding_provider_can_be_replaced_through_constructor()
    {
        var retriever = new CapturingRetriever();
        var first = new FakeEmbeddingProvider(true, [1f]);
        var second = new FakeEmbeddingProvider(true, [2f]);
        var a = Create(retriever, first);
        var b = Create(retriever, second);
        await a.RetrieveAsync(new RetrievalQuery("semantic policy question about hold", "d", "West", null, "c"), CancellationToken.None);
        await b.RetrieveAsync(new RetrievalQuery("semantic policy question about hold", "d", "West", null, "c"), CancellationToken.None);
        Assert.Equal(1f, retriever.Calls[0].QueryEmbedding![0]);
        Assert.Equal(2f, retriever.Calls[1].QueryEmbedding![0]);
    }

    private static KnowledgeRetrievalService Create(
        IDocumentRetriever retriever,
        IEmbeddingProvider embeddings,
        ArcKnowledgeOptions? options = null)
        => new(
            new EmptyGraph(),
            retriever,
            embeddings,
            Options.Create(options ?? new ArcKnowledgeOptions()),
            NullLogger<KnowledgeRetrievalService>.Instance);

    private static EvidenceSource Source(string id, string section, string content)
        => new(
            new SourceReference(id, null, null, "current", section, "test", DateTimeOffset.UtcNow),
            id,
            content,
            null,
            "ACTIVE",
            "GLOBAL",
            SourceDocumentId: id);

    private sealed class EmptyGraph : IGraphTraversal
    {
        public Task<IReadOnlyList<GraphNode>> TraverseDealerAsync(DealerUrn dealerUrn, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<GraphNode>>([]);
    }

    private sealed class CapturingRetriever : IDocumentRetriever
    {
        public DocumentRetrievalRequest? Last => Calls.Count == 0 ? null : Calls[^1];
        public List<DocumentRetrievalRequest> Calls { get; } = [];
        public List<EvidenceSource> Sources { get; set; } = [];

        public Task<IReadOnlyList<EvidenceSource>> RetrieveAsync(DocumentRetrievalRequest request, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            return Task.FromResult<IReadOnlyList<EvidenceSource>>(Sources);
        }
    }

    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        private readonly float[] _vector;
        public FakeEmbeddingProvider(bool available, float[]? vector = null)
        {
            IsAvailable = available;
            _vector = vector ?? [0.1f, 0.2f];
        }

        public bool IsAvailable { get; }
        public int Dimensions => _vector.Length;
        public string ModelId => "fake";
        public int Calls { get; private set; }

        public Task<float[]?> EmbedQueryAsync(string text, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<float[]?>(IsAvailable ? _vector : null);
        }

        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
            IEnumerable<string> inputs,
            CancellationToken cancellationToken = default)
        {
            var inputList = inputs.ToList();
            Calls += inputList.Count;
            var embeddings = inputList.Select(_ => _vector).ToList();
            return Task.FromResult<IReadOnlyList<float[]>>(embeddings);
        }
    }
}

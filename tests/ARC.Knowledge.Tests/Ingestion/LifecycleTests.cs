using ARC.Knowledge.Chunking;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Embeddings;
using ARC.Knowledge.Ingestion;
using ARC.Knowledge.Retrieval;
using ARC.Knowledge.Vector;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ARC.Knowledge.Tests.Ingestion;

/// <summary>
/// Phase 3 Stage 1: same-source/same-version chunk retirement and reactivation.
/// </summary>
public sealed class LifecycleTests
{
    private const string SourceId = "POLICY-P1";
    private const string VersionV1 = "V1";
    private const string VersionV2 = "V2";

    [Fact]
    public async Task Initial_ingestion_ABC_all_ACTIVE()
    {
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(new CountingEmbeddingProvider(), store);

        var result = await service.IngestDocumentAsync(Policy(AbcContent));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.ChunksProduced);
        Assert.All(await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1),
            d => Assert.Equal(ArcKnowledgeOptions.ActiveStatus, d.status));
        Assert.Contains(await store.QueryByDocumentTypeAsync("Policy"), d => d.id == Id("CLAUSE-1"));
        Assert.Contains(await store.QueryByDocumentTypeAsync("Policy"), d => d.id == Id("CLAUSE-2"));
        Assert.Contains(await store.QueryByDocumentTypeAsync("Policy"), d => d.id == Id("CLAUSE-3"));
    }

    [Fact]
    public async Task Same_source_version_reingest_AC_retires_B()
    {
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(new CountingEmbeddingProvider(), store);

        await service.IngestDocumentAsync(Policy(AbcContent));
        await service.IngestDocumentAsync(Policy(AcContent));

        var byId = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1))
            .ToDictionary(d => d.id);

        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, byId[Id("CLAUSE-1")].status);
        Assert.Equal(ArcKnowledgeOptions.InactiveStatus, byId[Id("CLAUSE-2")].status);
        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, byId[Id("CLAUSE-3")].status);
    }

    [Fact]
    public async Task Normal_retrieval_filter_excludes_retired_chunk()
    {
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(new CountingEmbeddingProvider(), store);

        await service.IngestDocumentAsync(Policy(AbcContent));
        await service.IngestDocumentAsync(Policy(AcContent));

        var filter = RetrievalSecurity.Create(
            new ArcKnowledgeOptions { PublishedPolicyVersion = VersionV1 },
            new RetrievalAuthorization("GLOBAL", null),
            requestedDealerUrn: null,
            requestedRegion: "GLOBAL",
            documentCategory: null);

        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, filter.Status);
        Assert.Equal(VersionV1, filter.Version);

        var active = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1))
            .Where(d => string.Equals(d.status, filter.Status, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(Id("CLAUSE-1"), active);
        Assert.DoesNotContain(Id("CLAUSE-2"), active);
        Assert.Contains(Id("CLAUSE-3"), active);
        Assert.Contains("c.status = 'ACTIVE'", CosmosDocumentQuery.SharedPredicates, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Different_version_does_not_retire_V1_chunks()
    {
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(new CountingEmbeddingProvider(), store);

        await service.IngestDocumentAsync(Policy(AbcContent, VersionV1));
        await service.IngestDocumentAsync(Policy(AcContent, VersionV2));

        var v1 = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1))
            .ToDictionary(d => d.id);
        var v2 = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV2))
            .ToDictionary(d => d.id);

        // Full V1 set remains; V2 upsert must not overwrite V1 identities.
        Assert.Equal(3, v1.Count);
        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, v1[Id("CLAUSE-1", VersionV1)].status);
        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, v1[Id("CLAUSE-2", VersionV1)].status);
        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, v1[Id("CLAUSE-3", VersionV1)].status);
        Assert.Equal(VersionV1, v1[Id("CLAUSE-2", VersionV1)].version);

        Assert.Equal(2, v2.Count);
        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, v2[Id("CLAUSE-1", VersionV2)].status);
        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, v2[Id("CLAUSE-3", VersionV2)].status);
        Assert.False(v2.ContainsKey(Id("CLAUSE-2", VersionV2)));
        Assert.NotEqual(Id("CLAUSE-1", VersionV1), Id("CLAUSE-1", VersionV2));
    }

    [Fact]
    public async Task Different_sourceDocumentId_is_unaffected()
    {
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(new CountingEmbeddingProvider(), store);

        await service.IngestDocumentAsync(Policy(AbcContent));
        await service.IngestDocumentAsync(Policy(AbcContent) with
        {
            SourceDocumentId = "POLICY-OTHER",
            Content = AcContent
        });

        var original = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1))
            .ToDictionary(d => d.id);

        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, original[Id("CLAUSE-1")].status);
        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, original[Id("CLAUSE-2")].status);
        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, original[Id("CLAUSE-3")].status);
    }

    [Fact]
    public async Task Unchanged_document_reingest_embeds_zero()
    {
        var embeddings = new CountingEmbeddingProvider();
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(embeddings, store);

        var first = await service.IngestDocumentAsync(Policy(AbcContent));
        var callsAfterFirst = embeddings.CallCount;
        var second = await service.IngestDocumentAsync(Policy(AbcContent));

        Assert.Equal(3, first.ChunksEmbedded);
        Assert.Equal(0, second.ChunksEmbedded);
        Assert.Equal(callsAfterFirst, embeddings.CallCount);
    }

    [Fact]
    public async Task One_clause_changed_embeds_exactly_one()
    {
        var embeddings = new CountingEmbeddingProvider();
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(embeddings, store);

        await service.IngestDocumentAsync(Policy(AbcContent));
        var callsAfterFirst = embeddings.CallCount;

        var changed = Policy("""
            1. Alpha
            Alpha body content for clause one.

            2. Beta
            Beta body content CHANGED for clause two.

            3. Gamma
            Gamma body content for clause three.
            """);

        var result = await service.IngestDocumentAsync(changed);

        Assert.Equal(1, result.ChunksEmbedded);
        Assert.Equal(callsAfterFirst + 1, embeddings.CallCount);
        Assert.Equal(2, result.ChunksSkippedUnchanged);
    }

    [Fact]
    public async Task One_clause_removed_retirement_embeds_zero()
    {
        var embeddings = new CountingEmbeddingProvider();
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(embeddings, store);

        await service.IngestDocumentAsync(Policy(AbcContent));
        var callsAfterFirst = embeddings.CallCount;

        var result = await service.IngestDocumentAsync(Policy(AcContent));

        Assert.Equal(0, result.ChunksEmbedded);
        Assert.Equal(callsAfterFirst, embeddings.CallCount);

        var b = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1))
            .Single(d => d.id == Id("CLAUSE-2"));
        Assert.Equal(ArcKnowledgeOptions.InactiveStatus, b.status);
        Assert.NotNull(b.embedding);
    }

    [Fact]
    public async Task One_clause_added_embeds_exactly_one()
    {
        var embeddings = new CountingEmbeddingProvider();
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(embeddings, store);

        await service.IngestDocumentAsync(Policy(AcContent));
        var callsAfterFirst = embeddings.CallCount;

        var result = await service.IngestDocumentAsync(Policy(AbcContent));

        Assert.Equal(1, result.ChunksEmbedded);
        Assert.Equal(callsAfterFirst + 1, embeddings.CallCount);
    }

    [Fact]
    public async Task Removed_clause_returns_later_reactivates_and_reuses_embedding()
    {
        var embeddings = new CountingEmbeddingProvider();
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(embeddings, store);

        await service.IngestDocumentAsync(Policy(AbcContent));
        await service.IngestDocumentAsync(Policy(AcContent));

        var retired = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1))
            .Single(d => d.id == Id("CLAUSE-2"));
        Assert.Equal(ArcKnowledgeOptions.InactiveStatus, retired.status);
        var priorEmbedding = retired.embedding;
        Assert.NotNull(priorEmbedding);

        var callsBeforeReactivate = embeddings.CallCount;
        var result = await service.IngestDocumentAsync(Policy(AbcContent));

        Assert.Equal(0, result.ChunksEmbedded);
        Assert.Equal(callsBeforeReactivate, embeddings.CallCount);

        var active = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1))
            .Single(d => d.id == Id("CLAUSE-2"));
        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, active.status);
        Assert.Same(priorEmbedding, active.embedding);

        var clause2Count = (await store.QueryByDocumentTypeAsync("Policy"))
            .Count(d => d.id == Id("CLAUSE-2"));
        Assert.Equal(1, clause2Count);
    }

    [Fact]
    public async Task Embedding_model_change_still_reembeds_active_incoming_chunks()
    {
        var store = new InMemoryIndexedDocumentStore();
        var service1 = CreateService(new CountingEmbeddingProvider(), store, "model1:3072d");
        var embeddings2 = new CountingEmbeddingProvider();
        var service2 = CreateService(embeddings2, store, "model2:3072d");

        await service1.IngestDocumentAsync(Policy(AbcContent));
        await service1.IngestDocumentAsync(Policy(AcContent)); // B retired under model1

        var result = await service2.IngestDocumentAsync(Policy(AcContent));

        Assert.Equal(2, result.ChunksEmbedded);
        Assert.Equal(2, embeddings2.CallCount);
        Assert.Equal(0, result.ChunksSkippedUnchanged);

        var b = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1))
            .Single(d => d.id == Id("CLAUSE-2"));
        Assert.Equal(ArcKnowledgeOptions.InactiveStatus, b.status);
    }

    [Fact]
    public async Task Retired_chunk_remains_stored_for_audit()
    {
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(new CountingEmbeddingProvider(), store);

        await service.IngestDocumentAsync(Policy(AbcContent));
        await service.IngestDocumentAsync(Policy(AcContent));

        var retired = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1))
            .Single(d => d.id == Id("CLAUSE-2"));

        Assert.Equal(ArcKnowledgeOptions.InactiveStatus, retired.status);
        Assert.False(string.IsNullOrWhiteSpace(retired.content));
        Assert.False(string.IsNullOrWhiteSpace(retired.contentHash));
        Assert.NotNull(retired.embedding);
        Assert.Equal(SourceId, retired.sourceDocumentId);
        Assert.Equal(VersionV1, retired.version);
        Assert.Equal("CLAUSE-2", retired.pageOrSection);
    }

    private static string Id(string pageOrSection, string version = VersionV1)
        => IndexedDocumentId.Create(SourceId, version, pageOrSection);

    private static string AbcContent => """
        1. Alpha
        Alpha body content for clause one.

        2. Beta
        Beta body content for clause two.

        3. Gamma
        Gamma body content for clause three.
        """;

    private static string AcContent => """
        1. Alpha
        Alpha body content for clause one.

        3. Gamma
        Gamma body content for clause three.
        """;

    private static SourceDocument Policy(string content, string version = VersionV1) => new()
    {
        SourceDocumentId = SourceId,
        DocumentType = "Policy",
        DocumentCategory = "RecoveryPolicy",
        Status = ArcKnowledgeOptions.ActiveStatus,
        Version = version,
        RegionScope = new[] { "GLOBAL" },
        BlobLocation = "policies/p1.pdf",
        Content = content
    };

    private static DocumentIngestionService CreateService(
        IEmbeddingProvider embeddingProvider,
        IIndexedDocumentStore documentStore,
        string embeddingModel = "test-model:3072d")
    {
        var options = Options.Create(new ArcKnowledgeOptions
        {
            Embeddings = new EmbeddingProviderOptions
            {
                Deployment = embeddingModel.Split(':')[0],
                Dimensions = 3072
            }
        });

        return new DocumentIngestionService(
            new IDocumentChunker[] { new PolicyClauseChunker(), new TemplateChunker() },
            embeddingProvider,
            documentStore,
            new NoOpContentSanitizer(),
            options,
            NullLogger<DocumentIngestionService>.Instance);
    }
}

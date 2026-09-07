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
/// Phase 3 Stage 1B: version-safe IndexedDocument ids and cross-version coexistence.
/// </summary>
public sealed class VersionSafeIdentityTests
{
    private const string SourceId = "POLICY-P1";
    private const string OtherSource = "POLICY-OTHER";
    private const string VersionV1 = "V1";
    private const string VersionV2 = "V2";

    [Fact]
    public void Same_source_version_section_yields_stable_id()
    {
        var a = IndexedDocumentId.Create(SourceId, VersionV1, "CLAUSE-1");
        var b = IndexedDocumentId.Create(SourceId, VersionV1, "CLAUSE-1");
        Assert.Equal(a, b);
        Assert.StartsWith("kd_", a, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_source_different_version_same_section_yields_different_ids()
    {
        var v1 = IndexedDocumentId.Create(SourceId, VersionV1, "CLAUSE-1");
        var v2 = IndexedDocumentId.Create(SourceId, VersionV2, "CLAUSE-1");
        Assert.NotEqual(v1, v2);
    }

    [Fact]
    public void Different_source_yields_different_ids()
    {
        var a = IndexedDocumentId.Create(SourceId, VersionV1, "CLAUSE-1");
        var b = IndexedDocumentId.Create(OtherSource, VersionV1, "CLAUSE-1");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task Content_change_keeps_same_logical_id()
    {
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(new CountingEmbeddingProvider(), store);

        await service.IngestDocumentAsync(Policy(AbcContent, VersionV1));
        var before = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1))
            .Single(d => d.pageOrSection == "CLAUSE-2").id;

        await service.IngestDocumentAsync(Policy("""
            1. Alpha
            Alpha body content for clause one.

            2. Beta
            Beta body content CHANGED for clause two.

            3. Gamma
            Gamma body content for clause three.
            """, VersionV1));

        var after = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1))
            .Single(d => d.pageOrSection == "CLAUSE-2");

        Assert.Equal(before, after.id);
        Assert.Contains("CHANGED", after.content, StringComparison.Ordinal);
        Assert.Equal(1, (await store.QueryByDocumentTypeAsync("Policy"))
            .Count(d => d.pageOrSection == "CLAUSE-2" && d.version == VersionV1));
    }

    [Fact]
    public async Task Embedding_model_change_keeps_same_document_id()
    {
        var store = new InMemoryIndexedDocumentStore();
        var service1 = CreateService(new CountingEmbeddingProvider(), store, "model1:3072d");
        var service2 = CreateService(new CountingEmbeddingProvider(), store, "model2:3072d");

        await service1.IngestDocumentAsync(Policy(AbcContent, VersionV1));
        var idBefore = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1))
            .Single(d => d.pageOrSection == "CLAUSE-1").id;

        await service2.IngestDocumentAsync(Policy(AbcContent, VersionV1));
        var after = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1))
            .Single(d => d.pageOrSection == "CLAUSE-1");

        Assert.Equal(idBefore, after.id);
        Assert.Equal("model2:3072d", after.embeddingModel);
    }

    [Fact]
    public async Task V1_ABC_and_V2_AC_coexist_without_overwrite()
    {
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(new CountingEmbeddingProvider(), store);

        await service.IngestDocumentAsync(Policy(AbcContent, VersionV1));
        await service.IngestDocumentAsync(Policy(AcContent, VersionV2));

        var v1 = await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1);
        var v2 = await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV2);

        Assert.Equal(3, v1.Count);
        Assert.Equal(2, v2.Count);
        Assert.All(v1, d => Assert.Equal(VersionV1, d.version));
        Assert.All(v2, d => Assert.Equal(VersionV2, d.version));
        Assert.Empty(v1.Select(d => d.id).Intersect(v2.Select(d => d.id)));
    }

    [Fact]
    public async Task V2_removal_retires_only_V2_B_leaving_V1_B_active()
    {
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(new CountingEmbeddingProvider(), store);

        await service.IngestDocumentAsync(Policy(AbcContent, VersionV1));
        await service.IngestDocumentAsync(Policy(AbcContent, VersionV2));
        await service.IngestDocumentAsync(Policy(AcContent, VersionV2));

        var v1B = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1))
            .Single(d => d.pageOrSection == "CLAUSE-2");
        var v2B = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV2))
            .Single(d => d.pageOrSection == "CLAUSE-2");

        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, v1B.status);
        Assert.Equal(ArcKnowledgeOptions.InactiveStatus, v2B.status);
        Assert.NotEqual(v1B.id, v2B.id);
    }

    [Fact]
    public async Task V2_reintroduces_B_reactivates_same_V2_id_without_duplicate()
    {
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(new CountingEmbeddingProvider(), store);

        await service.IngestDocumentAsync(Policy(AbcContent, VersionV2));
        await service.IngestDocumentAsync(Policy(AcContent, VersionV2));

        var retiredId = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV2))
            .Single(d => d.pageOrSection == "CLAUSE-2").id;

        await service.IngestDocumentAsync(Policy(AbcContent, VersionV2));

        var active = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV2))
            .Where(d => d.pageOrSection == "CLAUSE-2")
            .ToList();

        Assert.Single(active);
        Assert.Equal(retiredId, active[0].id);
        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, active[0].status);
    }

    [Fact]
    public async Task Identical_content_across_versions_creates_distinct_records_and_reuses_embedding()
    {
        var embeddings = new CountingEmbeddingProvider();
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(embeddings, store);

        await service.IngestDocumentAsync(Policy(AbcContent, VersionV1));
        var callsAfterV1 = embeddings.CallCount;
        var v1A = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV1))
            .Single(d => d.pageOrSection == "CLAUSE-1");

        var result = await service.IngestDocumentAsync(Policy(AbcContent, VersionV2));
        var v2A = (await store.QueryBySourceAndVersionAsync("Policy", SourceId, VersionV2))
            .Single(d => d.pageOrSection == "CLAUSE-1");

        Assert.NotEqual(v1A.id, v2A.id);
        Assert.Equal(0, result.ChunksEmbedded);
        Assert.Equal(callsAfterV1, embeddings.CallCount);
        Assert.Same(v1A.embedding, v2A.embedding);
    }

    [Fact]
    public async Task Current_version_retrieval_excludes_historical_version_chunks()
    {
        var store = new InMemoryIndexedDocumentStore();
        var service = CreateService(new CountingEmbeddingProvider(), store);

        await service.IngestDocumentAsync(Policy(AbcContent, VersionV1));
        await service.IngestDocumentAsync(Policy(AbcContent, VersionV2));

        var options = new ArcKnowledgeOptions { PublishedPolicyVersion = VersionV2 };
        var filter = RetrievalSecurity.Create(
            options,
            new RetrievalAuthorization("GLOBAL", null),
            requestedDealerUrn: null,
            requestedRegion: "GLOBAL",
            documentCategory: null);

        Assert.Equal(VersionV2, filter.Version);
        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, filter.Status);

        var visible = (await store.QueryByDocumentTypeAsync("Policy"))
            .Where(d =>
                string.Equals(d.status, filter.Status, StringComparison.OrdinalIgnoreCase)
                && string.Equals(d.version, filter.Version, StringComparison.Ordinal))
            .ToList();

        Assert.Equal(3, visible.Count);
        Assert.All(visible, d => Assert.Equal(VersionV2, d.version));
        Assert.Contains("c.version = @version", CosmosDocumentQuery.SharedPredicates, StringComparison.Ordinal);
    }

    [Fact]
    public void Special_characters_produce_deterministic_cosmos_safe_id()
    {
        var source = @"policy/P1#weird\name?";
        var version = @"v2/beta#1";
        var section = @"CLAUSE/1?#A";

        var a = IndexedDocumentId.Create(source, version, section);
        var b = IndexedDocumentId.Create(source, version, section);

        Assert.Equal(a, b);
        Assert.StartsWith("kd_", a, StringComparison.Ordinal);
        Assert.DoesNotContain('/', a);
        Assert.DoesNotContain('\\', a);
        Assert.DoesNotContain('?', a);
        Assert.DoesNotContain('#', a);
        Assert.Matches("^kd_[0-9a-f]{64}$", a);
    }

    [Fact]
    public void Whitespace_trimmed_inputs_do_not_change_id()
    {
        var a = IndexedDocumentId.Create(" POLICY-P1 ", " V1 ", " CLAUSE-1 ");
        var b = IndexedDocumentId.Create("POLICY-P1", "V1", "CLAUSE-1");
        Assert.Equal(a, b);
    }

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

    private static SourceDocument Policy(string content, string version) => new()
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

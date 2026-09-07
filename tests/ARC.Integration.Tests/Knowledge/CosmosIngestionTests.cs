using ARC.Data.Cosmos;
using ARC.Integration.Tests.Fixtures;
using ARC.Knowledge.Chunking;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Embeddings;
using ARC.Knowledge.Ingestion;
using ARC.Knowledge.Vector;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ARC.Integration.Tests.Knowledge;

/// <summary>
/// Cosmos DB integration tests for Phase 2 ingestion: upsert, hash lookup, vector storage.
/// Requires live Cosmos or emulator. Marked NOT RUN if unavailable.
/// </summary>
[Collection("CosmosCollection")]
public sealed class CosmosIngestionTests
{
    private readonly CosmosFixture _fixture;

    public CosmosIngestionTests(CosmosFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UpsertAsync_StoresDocumentWithEmbedding()
    {
        // Arrange
        using var client = InfrastructureGate.CreateCosmosClient(_fixture.ConnectionString);
        var store = new CosmosIndexedDocumentStore(client, _fixture.DatabaseId);
        var document = new IndexedDocument
        {
            id = "test-chunk-001",
            documentType = "Policy",
            documentCategory = "RecoveryPolicy",
            status = "ACTIVE",
            version = "current",
            regionScope = new[] { "GLOBAL" },
            sourceDocumentId = "POLICY-001",
            pageOrSection = "CLAUSE-1",
            title = "Test Clause",
            content = "This is test content.",
            contentHash = "abc123",
            embedding = CreateTestEmbedding(3072),
            embeddingModel = "test-model:3072d",
            embeddedUtc = DateTimeOffset.UtcNow
        };

        // Act
        await store.UpsertAsync(document);

        // Assert
        var retrieved = await RetrieveDocumentDirectlyAsync(client, document.id, document.documentType);
        Assert.NotNull(retrieved);
        Assert.Equal(document.content, retrieved.content);
        Assert.Equal(document.contentHash, retrieved.contentHash);
        Assert.NotNull(retrieved.embedding);
        Assert.Equal(3072, retrieved.embedding.Length);
    }

    [Fact]
    public async Task FindByContentHashAsync_FindsExistingDocument()
    {
        // Arrange
        using var client = InfrastructureGate.CreateCosmosClient(_fixture.ConnectionString);
        var store = new CosmosIndexedDocumentStore(client, _fixture.DatabaseId);
        var contentHash = "unique-hash-123";
        var embeddingModel = "test-model:3072d";
        
        var document = new IndexedDocument
        {
            id = "test-chunk-002",
            documentType = "Policy",
            status = "ACTIVE",
            content = "Test content",
            contentHash = contentHash,
            embedding = CreateTestEmbedding(3072),
            embeddingModel = embeddingModel,
            embeddedUtc = DateTimeOffset.UtcNow
        };

        await store.UpsertAsync(document);

        // Act
        var found = await store.FindByContentHashAsync(contentHash, embeddingModel);

        // Assert
        Assert.NotNull(found);
        Assert.Equal(contentHash, found.contentHash);
        Assert.Equal(embeddingModel, found.embeddingModel);
    }

    [Fact]
    public async Task FindByContentHashAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        using var client = InfrastructureGate.CreateCosmosClient(_fixture.ConnectionString);
        var store = new CosmosIndexedDocumentStore(client, _fixture.DatabaseId);

        // Act
        var found = await store.FindByContentHashAsync("nonexistent-hash", "model");

        // Assert
        Assert.Null(found);
    }

    [Fact]
    public async Task UpsertAsync_Idempotent_UpdatesExistingDocument()
    {
        // Arrange
        using var client = InfrastructureGate.CreateCosmosClient(_fixture.ConnectionString);
        var store = new CosmosIndexedDocumentStore(client, _fixture.DatabaseId);
        var document = new IndexedDocument
        {
            id = "test-chunk-003",
            documentType = "Policy",
            status = "ACTIVE",
            content = "Original content",
            contentHash = "hash1",
            embedding = CreateTestEmbedding(3072),
            embeddingModel = "model1",
            embeddedUtc = DateTimeOffset.UtcNow
        };

        await store.UpsertAsync(document);

        // Act - update same document
        document.content = "Updated content";
        document.contentHash = "hash2";
        await store.UpsertAsync(document);

        // Assert
        var retrieved = await RetrieveDocumentDirectlyAsync(client, document.id, document.documentType);
        Assert.NotNull(retrieved);
        Assert.Equal("Updated content", retrieved.content);
        Assert.Equal("hash2", retrieved.contentHash);
    }

    [Fact]
    public async Task UpsertBatchAsync_StoresMultipleDocuments()
    {
        // Arrange
        using var client = InfrastructureGate.CreateCosmosClient(_fixture.ConnectionString);
        var store = new CosmosIndexedDocumentStore(client, _fixture.DatabaseId);
        var documents = new[]
        {
            new IndexedDocument
            {
                id = "batch-001",
                documentType = "Policy",
                status = "ACTIVE",
                content = "Content 1",
                contentHash = "hash-batch-1",
                embedding = CreateTestEmbedding(3072),
                embeddingModel = "model1"
            },
            new IndexedDocument
            {
                id = "batch-002",
                documentType = "Policy",
                status = "ACTIVE",
                content = "Content 2",
                contentHash = "hash-batch-2",
                embedding = CreateTestEmbedding(3072),
                embeddingModel = "model1"
            }
        };

        // Act
        await store.UpsertBatchAsync(documents);

        // Assert
        var allDocuments = await store.QueryByDocumentTypeAsync("Policy");
        Assert.Contains(allDocuments, d => d.id == "batch-001");
        Assert.Contains(allDocuments, d => d.id == "batch-002");
    }

    [Fact]
    public async Task QueryByDocumentTypeAsync_ReturnsMatchingDocuments()
    {
        // Arrange
        using var client = InfrastructureGate.CreateCosmosClient(_fixture.ConnectionString);
        var store = new CosmosIndexedDocumentStore(client, _fixture.DatabaseId);
        var document = new IndexedDocument
        {
            id = "query-test-001",
            documentType = "Template",
            status = "ACTIVE",
            content = "Query test content",
            contentHash = "hash-query",
            embedding = CreateTestEmbedding(3072),
            embeddingModel = "model1"
        };

        await store.UpsertAsync(document);

        // Act
        var results = await store.QueryByDocumentTypeAsync("Template");

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, d => d.id == "query-test-001");
    }

    [Fact]
    public async Task UpsertAsync_ValidatesRequiredFields()
    {
        // Arrange
        using var client = InfrastructureGate.CreateCosmosClient(_fixture.ConnectionString);
        var store = new CosmosIndexedDocumentStore(client, _fixture.DatabaseId);

        // Act & Assert - missing id
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await store.UpsertAsync(new IndexedDocument
            {
                id = "",
                documentType = "Policy"
            });
        });

        // Act & Assert - missing documentType (partition key)
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await store.UpsertAsync(new IndexedDocument
            {
                id = "test",
                documentType = ""
            });
        });
    }

    [Fact]
    public async Task SameVersion_Reingest_PersistsRetiredChunk_WithoutHardDelete()
    {
        await _fixture.InitializeAsync(CancellationToken.None);

        using var client = InfrastructureGate.CreateCosmosClient(_fixture.ConnectionString);
        var store = new CosmosIndexedDocumentStore(client, _fixture.DatabaseId);
        var sourceId = $"POLICY-LIFE-{Guid.NewGuid():N}";
        const string version = "V1";

        var service = CreateIngestionService(store);

        await service.IngestDocumentAsync(CreateLifecyclePolicy(sourceId, version, AbcContent));
        await service.IngestDocumentAsync(CreateLifecyclePolicy(sourceId, version, AcContent));

        var scoped = await store.QueryBySourceAndVersionAsync("Policy", sourceId, version);
        var bySection = scoped.ToDictionary(d => d.pageOrSection!);

        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, bySection["CLAUSE-1"].status);
        Assert.Equal(ArcKnowledgeOptions.InactiveStatus, bySection["CLAUSE-2"].status);
        Assert.Equal(ArcKnowledgeOptions.ActiveStatus, bySection["CLAUSE-3"].status);

        var retiredId = IndexedDocumentId.Create(sourceId, version, "CLAUSE-2");
        Assert.Equal(retiredId, bySection["CLAUSE-2"].id);

        var retired = await RetrieveDocumentDirectlyAsync(client, retiredId, "Policy");
        Assert.NotNull(retired);
        Assert.Equal(ArcKnowledgeOptions.InactiveStatus, retired.status);
        Assert.NotNull(retired.embedding);
        Assert.False(string.IsNullOrWhiteSpace(retired.contentHash));
    }

    [Fact]
    public async Task V1_and_V2_SameClause_Coexist_WithDistinctIds()
    {
        await _fixture.InitializeAsync(CancellationToken.None);

        using var client = InfrastructureGate.CreateCosmosClient(_fixture.ConnectionString);
        var store = new CosmosIndexedDocumentStore(client, _fixture.DatabaseId);
        var sourceId = $"POLICY-VER-{Guid.NewGuid():N}";

        var service = CreateIngestionService(store);

        await service.IngestDocumentAsync(CreateLifecyclePolicy(sourceId, "V1", AbcContent));
        await service.IngestDocumentAsync(CreateLifecyclePolicy(sourceId, "V2", AcContent));

        var v1 = await store.QueryBySourceAndVersionAsync("Policy", sourceId, "V1");
        var v2 = await store.QueryBySourceAndVersionAsync("Policy", sourceId, "V2");

        Assert.Equal(3, v1.Count);
        Assert.Equal(2, v2.Count);
        Assert.Empty(v1.Select(d => d.id).Intersect(v2.Select(d => d.id)));

        var v1A = v1.Single(d => d.pageOrSection == "CLAUSE-1");
        var v2A = v2.Single(d => d.pageOrSection == "CLAUSE-1");
        Assert.NotEqual(v1A.id, v2A.id);
        Assert.Equal(IndexedDocumentId.Create(sourceId, "V1", "CLAUSE-1"), v1A.id);
        Assert.Equal(IndexedDocumentId.Create(sourceId, "V2", "CLAUSE-1"), v2A.id);

        var persistedV1 = await RetrieveDocumentDirectlyAsync(client, v1A.id, "Policy");
        var persistedV2 = await RetrieveDocumentDirectlyAsync(client, v2A.id, "Policy");
        Assert.NotNull(persistedV1);
        Assert.NotNull(persistedV2);
        Assert.Equal("V1", persistedV1.version);
        Assert.Equal("V2", persistedV2.version);
    }

    private static DocumentIngestionService CreateIngestionService(IIndexedDocumentStore store)
    {
        var options = Options.Create(new ArcKnowledgeOptions
        {
            Embeddings = new EmbeddingProviderOptions
            {
                Deployment = "test-model",
                Dimensions = 3072
            }
        });

        return new DocumentIngestionService(
            new IDocumentChunker[] { new PolicyClauseChunker(), new TemplateChunker() },
            new DeterministicEmbeddingProvider(),
            store,
            new NoOpContentSanitizer(),
            options,
            NullLogger<DocumentIngestionService>.Instance);
    }

    private static SourceDocument CreateLifecyclePolicy(string sourceId, string version, string content) => new()
    {
        SourceDocumentId = sourceId,
        DocumentType = "Policy",
        DocumentCategory = "RecoveryPolicy",
        Status = ArcKnowledgeOptions.ActiveStatus,
        Version = version,
        RegionScope = new[] { "GLOBAL" },
        BlobLocation = "policies/lifecycle.pdf",
        Content = content
    };

    private const string AbcContent = """
        1. Alpha
        Alpha body content for clause one.

        2. Beta
        Beta body content for clause two.

        3. Gamma
        Gamma body content for clause three.
        """;

    private const string AcContent = """
        1. Alpha
        Alpha body content for clause one.

        3. Gamma
        Gamma body content for clause three.
        """;

    private async Task<IndexedDocument?> RetrieveDocumentDirectlyAsync(
        CosmosClient client,
        string id,
        string documentType)
    {
        try
        {
            var container = client
                .GetDatabase(_fixture.DatabaseId)
                .GetContainer(CosmosDocumentsContract.ContainerName);

            var response = await container.ReadItemAsync<IndexedDocument>(
                id,
                new PartitionKey(documentType));

            return response.Resource;
        }
        catch (CosmosException)
        {
            return null;
        }
    }

    private static float[] CreateTestEmbedding(int dimensions)
    {
        return Enumerable.Range(0, dimensions)
            .Select(i => (float)i / dimensions)
            .ToArray();
    }

    private sealed class DeterministicEmbeddingProvider : IEmbeddingProvider
    {
        public bool IsAvailable => true;
        public int Dimensions => 3072;
        public string ModelId => "test-model:3072d";

        public Task<float[]?> EmbedQueryAsync(string text, CancellationToken cancellationToken)
            => Task.FromResult<float[]?>(CreateTestEmbedding(3072));

        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
            IEnumerable<string> inputs,
            CancellationToken cancellationToken = default)
        {
            var embeddings = inputs.Select(_ => CreateTestEmbedding(3072)).ToList();
            return Task.FromResult<IReadOnlyList<float[]>>(embeddings);
        }
    }
}

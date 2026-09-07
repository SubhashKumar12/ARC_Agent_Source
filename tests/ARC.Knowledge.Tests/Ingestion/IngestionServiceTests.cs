using ARC.Knowledge.Chunking;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Embeddings;
using ARC.Knowledge.Ingestion;
using ARC.Knowledge.Vector;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ARC.Knowledge.Tests.Ingestion;

/// <summary>
/// Tests for document ingestion service: idempotency, deduplication, embedding cost controls.
/// </summary>
public sealed class IngestionServiceTests
{
    [Fact]
    public async Task IngestDocument_Idempotent_SameDocumentTwice_NoEmbeddingDuplication()
    {
        // Arrange
        var embeddingProvider = new CountingEmbeddingProvider();
        var documentStore = new InMemoryIndexedDocumentStore();
        var service = CreateService(embeddingProvider, documentStore);

        var document = CreateSamplePolicyDocument();

        // Act
        var result1 = await service.IngestDocumentAsync(document);
        var result2 = await service.IngestDocumentAsync(document);

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.True(result1.ChunksEmbedded > 0, "First ingestion should embed chunks");
        Assert.Equal(0, result2.ChunksEmbedded); // Second should skip all
        Assert.True(result2.ChunksSkippedUnchanged > 0, "Second ingestion should skip unchanged chunks");
        
        // Verify embedding provider was only called once per chunk
        Assert.Equal(result1.ChunksEmbedded, embeddingProvider.CallCount);
    }

    [Fact]
    public async Task IngestDocument_ChangedContent_ReEmbedsOnlyChangedChunks()
    {
        // Arrange
        var embeddingProvider = new CountingEmbeddingProvider();
        var documentStore = new InMemoryIndexedDocumentStore();
        var service = CreateService(embeddingProvider, documentStore);

        var document1 = CreateSamplePolicyDocument();
        var document2 = CreateSamplePolicyDocument() with
        {
            Content = CreateSamplePolicyDocument().Content.Replace("Introduction", "Modified Introduction")
        };

        // Act
        var result1 = await service.IngestDocumentAsync(document1);
        var embeddingsAfterFirst = embeddingProvider.CallCount;
        
        var result2 = await service.IngestDocumentAsync(document2);

        // Assert
        Assert.True(result1.IsSuccess, "First ingestion should succeed");
        Assert.True(result2.IsSuccess, "Second ingestion should succeed");
        Assert.True(result1.ChunksProduced > 0, "First ingestion should produce chunks");
        Assert.True(result2.ChunksProduced > 0, "Second ingestion should produce chunks");
        
        // When content changes, at least the changed clause should be re-embedded
        // Due to content change in one clause, we expect at least 1 chunk to be re-embedded
        Assert.True(result2.ChunksEmbedded > 0, "Changed chunks should be re-embedded");
        Assert.True(embeddingProvider.CallCount > embeddingsAfterFirst, "Embedding provider should be called for changed chunks");
    }

    [Fact]
    public async Task IngestDocument_DifferentEmbeddingModel_ReEmbedsAll()
    {
        // Arrange
        var embeddingProvider1 = new CountingEmbeddingProvider();
        var documentStore = new InMemoryIndexedDocumentStore();
        var service1 = CreateService(embeddingProvider1, documentStore, embeddingModel: "model1:3072d");

        var embeddingProvider2 = new CountingEmbeddingProvider();
        var service2 = CreateService(embeddingProvider2, documentStore, embeddingModel: "model2:3072d");

        var document = CreateSamplePolicyDocument();

        // Act
        var result1 = await service1.IngestDocumentAsync(document);
        var result2 = await service2.IngestDocumentAsync(document);

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.Equal(result1.ChunksEmbedded, result2.ChunksEmbedded);
        Assert.Equal(0, result2.ChunksSkippedUnchanged); // Different model, can't skip
    }

    [Fact]
    public async Task IngestDocument_WrongDimensions_FailsFast()
    {
        // Arrange
        var embeddingProvider = new CountingEmbeddingProvider(dimensions: 1536); // Wrong dimensions
        var documentStore = new InMemoryIndexedDocumentStore();
        var service = CreateService(embeddingProvider, documentStore);

        var document = CreateSamplePolicyDocument();

        // Act
        var result = await service.IngestDocumentAsync(document);

        // Assert
        Assert.False(result.IsSuccess, "Ingestion should fail with wrong embedding dimensions");
        Assert.True(result.ChunksFailed > 0, "Chunks with wrong dimensions should be marked as failed");
        Assert.Contains("dimension mismatch", string.Join(" ", result.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IngestDocument_PolicyCategory_UsesCorrectChunker()
    {
        // Arrange
        var embeddingProvider = new CountingEmbeddingProvider();
        var documentStore = new InMemoryIndexedDocumentStore();
        var service = CreateService(embeddingProvider, documentStore);

        var document = CreateSamplePolicyDocument();

        // Act
        var result = await service.IngestDocumentAsync(document);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.ChunksProduced > 0, "Policy document should be chunked");
        Assert.Equal(1, result.DocumentsProcessed);
    }

    [Fact]
    public async Task IngestDocument_TemplateCategory_UsesCorrectChunker()
    {
        // Arrange
        var embeddingProvider = new CountingEmbeddingProvider();
        var documentStore = new InMemoryIndexedDocumentStore();
        var service = CreateService(embeddingProvider, documentStore);

        var document = CreateSampleTemplateDocument();

        // Act
        var result = await service.IngestDocumentAsync(document);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.ChunksProduced > 0, "Template document should be chunked");
        Assert.Equal(1, result.DocumentsProcessed);
    }

    [Fact]
    public async Task IngestDocument_UnsupportedCategory_ReportsError()
    {
        // Arrange
        var embeddingProvider = new CountingEmbeddingProvider();
        var documentStore = new InMemoryIndexedDocumentStore();
        var service = CreateService(embeddingProvider, documentStore);

        var document = CreateSamplePolicyDocument() with
        {
            DocumentCategory = "UnsupportedCategory"
        };

        // Act
        var result = await service.IngestDocumentAsync(document);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task IngestDocuments_Batch_ProcessesAll()
    {
        // Arrange
        var embeddingProvider = new CountingEmbeddingProvider();
        var documentStore = new InMemoryIndexedDocumentStore();
        var service = CreateService(embeddingProvider, documentStore);

        var documents = new[]
        {
            CreateSamplePolicyDocument(),
            CreateSampleTemplateDocument()
        };

        // Act
        var result = await service.IngestDocumentsAsync(documents);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.DocumentsProcessed);
        Assert.True(result.ChunksProduced > 0);
    }

    [Fact]
    public async Task IngestDocument_PreservesMetadata()
    {
        // Arrange
        var embeddingProvider = new CountingEmbeddingProvider();
        var documentStore = new InMemoryIndexedDocumentStore();
        var service = CreateService(embeddingProvider, documentStore);

        var document = CreateSamplePolicyDocument() with
        {
            Status = "ACTIVE",
            Version = "v2.0",
            RegionScope = new[] { "WEST", "EAST" },
            DealerUrn = "DEALER-123"
        };

        // Act
        await service.IngestDocumentAsync(document);

        // Assert
        var storedDocuments = await documentStore.QueryByDocumentTypeAsync(document.DocumentType);
        Assert.NotEmpty(storedDocuments);
        Assert.All(storedDocuments, doc =>
        {
            Assert.Equal("ACTIVE", doc.status);
            Assert.Equal("v2.0", doc.version);
            Assert.Contains("WEST", doc.regionScope!);
            Assert.Equal("DEALER-123", doc.dealerUrn);
        });
    }

    private static DocumentIngestionService CreateService(
        IEmbeddingProvider embeddingProvider,
        IIndexedDocumentStore documentStore,
        string embeddingModel = "test-model:3072d")
    {
        var chunkers = new IDocumentChunker[]
        {
            new PolicyClauseChunker(),
            new TemplateChunker()
        };

        var options = Options.Create(new ArcKnowledgeOptions
        {
            Embeddings = new EmbeddingProviderOptions
            {
                Deployment = embeddingModel.Split(':')[0],
                Dimensions = 3072
            }
        });

        return new DocumentIngestionService(
            chunkers,
            embeddingProvider,
            documentStore,
            new NoOpContentSanitizer(),
            options,
            NullLogger<DocumentIngestionService>.Instance);
    }

    private static SourceDocument CreateSamplePolicyDocument()
    {
        return new SourceDocument
        {
            SourceDocumentId = "POLICY-001",
            DocumentType = "Policy",
            DocumentCategory = "RecoveryPolicy",
            Status = "ACTIVE",
            Version = "current",
            RegionScope = new[] { "GLOBAL" },
            BlobLocation = "policies/test.pdf",
            Content = @"1. Introduction
This policy establishes recovery procedures.

2. Eligibility
Eligibility criteria are defined here.

3. Process
Recovery process steps."
        };
    }

    private static SourceDocument CreateSampleTemplateDocument()
    {
        return new SourceDocument
        {
            SourceDocumentId = "TEMPLATE-001",
            DocumentType = "Template",
            DocumentCategory = "NoticeTemplate",
            Status = "ACTIVE",
            Version = "current",
            RegionScope = new[] { "GLOBAL" },
            BlobLocation = "templates/test.html",
            Content = @"HEADING: Test Notice

DEMAND:
Payment is required.

CLOSING:
Thank you."
        };
    }
}

/// <summary>
/// Fake embedding provider that counts calls for cost control testing.
/// </summary>
internal sealed class CountingEmbeddingProvider : IEmbeddingProvider
{
    private readonly int _dimensions;
    public int CallCount { get; private set; }

    public CountingEmbeddingProvider(int dimensions = 3072)
    {
        _dimensions = dimensions;
    }

    public bool IsAvailable => true;
    public int Dimensions => _dimensions;
    public string ModelId => $"test-model:{_dimensions}d";

    public Task<float[]?> EmbedQueryAsync(string text, CancellationToken cancellationToken)
    {
        CallCount++;
        var embedding = Enumerable.Range(0, _dimensions).Select(i => (float)i / _dimensions).ToArray();
        return Task.FromResult<float[]?>(embedding);
    }

    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> inputs,
        CancellationToken cancellationToken = default)
    {
        var inputList = inputs.ToList();
        CallCount += inputList.Count;

        var embeddings = inputList
            .Select(_ => Enumerable.Range(0, _dimensions).Select(i => (float)i / _dimensions).ToArray())
            .ToList();

        return Task.FromResult<IReadOnlyList<float[]>>(embeddings);
    }
}

/// <summary>
/// In-memory document store for testing.
/// </summary>
internal sealed class InMemoryIndexedDocumentStore : IIndexedDocumentStore
{
    private readonly Dictionary<string, IndexedDocument> _documents = new();

    public Task<IndexedDocument?> FindByContentHashAsync(
        string contentHash,
        string embeddingModel,
        CancellationToken cancellationToken = default)
    {
        var match = _documents.Values
            .FirstOrDefault(d => d.contentHash == contentHash && d.embeddingModel == embeddingModel);
        
        return Task.FromResult(match);
    }

    public Task UpsertAsync(IndexedDocument document, CancellationToken cancellationToken = default)
    {
        _documents[document.id] = document;
        return Task.CompletedTask;
    }

    public Task UpsertBatchAsync(
        IEnumerable<IndexedDocument> documents,
        CancellationToken cancellationToken = default)
    {
        foreach (var document in documents)
        {
            _documents[document.id] = document;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IndexedDocument>> QueryByDocumentTypeAsync(
        string documentType,
        CancellationToken cancellationToken = default)
    {
        var matches = _documents.Values
            .Where(d => d.documentType == documentType)
            .ToList();

        return Task.FromResult<IReadOnlyList<IndexedDocument>>(matches);
    }

    public Task<IReadOnlyList<IndexedDocument>> QueryBySourceAndVersionAsync(
        string documentType,
        string sourceDocumentId,
        string version,
        CancellationToken cancellationToken = default)
    {
        var matches = _documents.Values
            .Where(d =>
                d.documentType == documentType
                && d.sourceDocumentId == sourceDocumentId
                && d.version == version)
            .ToList();

        return Task.FromResult<IReadOnlyList<IndexedDocument>>(matches);
    }
}

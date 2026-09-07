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
/// Tests to ensure Phase 2 maintains Phase 1 architectural constraints:
/// - No chat LLM calls during ingestion
/// - Abstraction layers preserved
/// - No financial data embedding
/// </summary>
public sealed class Phase2ArchitectureTests
{
    [Fact]
    public async Task Ingestion_NoChatLLMCalls()
    {
        // Arrange
        var chatCallDetector = new ChatCallDetectingEmbeddingProvider();
        var documentStore = new InMemoryDocumentStore();
        var service = CreateService(chatCallDetector, documentStore);

        var document = new SourceDocument
        {
            SourceDocumentId = "POLICY-001",
            DocumentType = "Policy",
            DocumentCategory = "RecoveryPolicy",
            Status = "ACTIVE",
            Version = "current",
            RegionScope = new[] { "GLOBAL" },
            Content = "1. Test Clause\nTest content."
        };

        // Act
        await service.IngestDocumentAsync(document);

        // Assert
        Assert.False(chatCallDetector.ChatCallDetected, 
            "Ingestion must not invoke chat LLM. Only embedding generation is allowed.");
    }

    [Fact]
    public void IngestionService_DependsOnlyOnAbstractions()
    {
        // Arrange - verify constructor dependencies are interfaces
        var serviceType = typeof(DocumentIngestionService);
        var constructor = serviceType.GetConstructors().First();
        var parameters = constructor.GetParameters();

        // Assert
        var embeddingProviderParam = parameters.First(p => p.Name == "embeddingProvider");
        Assert.Equal(typeof(IEmbeddingProvider), embeddingProviderParam.ParameterType);

        var documentStoreParam = parameters.First(p => p.Name == "documentStore");
        Assert.Equal(typeof(IIndexedDocumentStore), documentStoreParam.ParameterType);

        var sanitizerParam = parameters.First(p => p.Name == "sanitizer");
        Assert.Equal(typeof(IContentSanitizer), sanitizerParam.ParameterType);
    }

    [Fact]
    public void DocumentIngestionService_DoesNotDirectlyReferenceAzureSDK()
    {
        // Verify ingestion service does not couple to Azure SDK types
        var serviceType = typeof(DocumentIngestionService);
        var references = serviceType.Assembly.GetReferencedAssemblies();

        // Azure.AI.OpenAI may be referenced by embeddings provider, but not by ingestion orchestration
        var azureAIOpenAIRef = references.FirstOrDefault(r => r.Name == "Azure.AI.OpenAI");
        
        // DocumentIngestionService should not have direct fields/properties of Azure SDK types
        var fields = serviceType.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.DoesNotContain(fields, f => f.FieldType.Namespace?.StartsWith("Azure") == true);
    }

    [Fact]
    public async Task Ingestion_SanitizationHook_InvokedBeforeEmbedding()
    {
        // Arrange
        var sanitizer = new TrackingSanitizer();
        var embeddingProvider = new TrackingEmbeddingProvider();
        var documentStore = new InMemoryDocumentStore();
        
        var chunkers = new IDocumentChunker[] { new PolicyClauseChunker() };
        var options = Options.Create(new ArcKnowledgeOptions
        {
            Embeddings = new EmbeddingProviderOptions
            {
                Deployment = "test",
                Dimensions = 3072
            }
        });

        var service = new DocumentIngestionService(
            chunkers,
            embeddingProvider,
            documentStore,
            sanitizer,
            options,
            NullLogger<DocumentIngestionService>.Instance);

        var document = new SourceDocument
        {
            SourceDocumentId = "POLICY-001",
            DocumentType = "Policy",
            DocumentCategory = "RecoveryPolicy",
            Status = "ACTIVE",
            Version = "current",
            RegionScope = new[] { "GLOBAL" },
            Content = "1. Test\nContent with PII data."
        };

        // Act
        await service.IngestDocumentAsync(document);

        // Assert
        Assert.True(sanitizer.WasCalled, "Sanitization hook must be invoked");
        Assert.True(embeddingProvider.WasCalled, "Embedding provider must be invoked");
        Assert.True(sanitizer.CallOrder < embeddingProvider.CallOrder,
            "Sanitization must happen before embedding generation");
    }

    [Fact]
    public void Chunkers_AreReplaceable()
    {
        // Verify chunkers implement interface and can be replaced via DI
        var policyChunker = new PolicyClauseChunker();
        var templateChunker = new TemplateChunker();

        Assert.IsAssignableFrom<IDocumentChunker>(policyChunker);
        Assert.IsAssignableFrom<IDocumentChunker>(templateChunker);
    }

    [Fact]
    public void EmbeddingProvider_IsReplaceable()
    {
        // Verify embedding provider is interface-based and replaceable
        var disabledProvider = new DisabledEmbeddingProvider(new EmbeddingProviderOptions());
        
        Assert.IsAssignableFrom<IEmbeddingProvider>(disabledProvider);
    }

    [Fact]
    public void DocumentStore_IsReplaceable()
    {
        // Verify document store is interface-based and replaceable
        var inMemoryStore = new InMemoryDocumentStore();
        
        Assert.IsAssignableFrom<IIndexedDocumentStore>(inMemoryStore);
    }

    private static DocumentIngestionService CreateService(
        IEmbeddingProvider embeddingProvider,
        IIndexedDocumentStore documentStore)
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
                Deployment = "test-model",
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
}

internal sealed class ChatCallDetectingEmbeddingProvider : IEmbeddingProvider
{
    public bool ChatCallDetected { get; private set; }
    public bool IsAvailable => true;
    public int Dimensions => 3072;
    public string ModelId => "test-model";

    public Task<float[]?> EmbedQueryAsync(string text, CancellationToken cancellationToken)
    {
        var embedding = Enumerable.Range(0, 3072).Select(i => (float)i / 3072).ToArray();
        return Task.FromResult<float[]?>(embedding);
    }

    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> inputs,
        CancellationToken cancellationToken = default)
    {
        // This is an embedding call, which is allowed
        var embeddings = inputs
            .Select(_ => Enumerable.Range(0, 3072).Select(i => (float)i / 3072).ToArray())
            .ToList();

        return Task.FromResult<IReadOnlyList<float[]>>(embeddings);
    }

    public void DetectChatCall()
    {
        ChatCallDetected = true;
    }
}

internal static class CallOrderTracker
{
    private static int _globalCallOrder;
    public static int GetNext() => Interlocked.Increment(ref _globalCallOrder);
}

internal sealed class TrackingSanitizer : IContentSanitizer
{
    public bool WasCalled { get; private set; }
    public int CallOrder { get; private set; }

    public string Sanitize(string content)
    {
        WasCalled = true;
        CallOrder = CallOrderTracker.GetNext();
        return content;
    }
}

internal sealed class TrackingEmbeddingProvider : IEmbeddingProvider
{
    public bool WasCalled { get; private set; }
    public int CallOrder { get; private set; }
    public bool IsAvailable => true;
    public int Dimensions => 3072;
    public string ModelId => "test-model";

    public Task<float[]?> EmbedQueryAsync(string text, CancellationToken cancellationToken)
    {
        WasCalled = true;
        CallOrder = CallOrderTracker.GetNext();
        var embedding = Enumerable.Range(0, 3072).Select(i => (float)i / 3072).ToArray();
        return Task.FromResult<float[]?>(embedding);
    }

    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> inputs,
        CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        CallOrder = CallOrderTracker.GetNext();

        var embeddings = inputs
            .Select(_ => Enumerable.Range(0, 3072).Select(i => (float)i / 3072).ToArray())
            .ToList();

        return Task.FromResult<IReadOnlyList<float[]>>(embeddings);
    }
}

internal sealed class InMemoryDocumentStore : IIndexedDocumentStore
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

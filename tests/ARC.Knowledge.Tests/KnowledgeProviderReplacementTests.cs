using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ARC.Data.Blob;
using ARC.Data.Sql;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.DependencyInjection;
using ARC.Knowledge.Documents;
using ARC.Knowledge.Embeddings;
using ARC.Knowledge.Retrieval;

namespace ARC.Knowledge.Tests;

public sealed class KnowledgeProviderReplacementTests
{
    [Fact]
    public void Default_embedding_dimensions_are_3072()
    {
        var options = new ArcKnowledgeOptions();
        Assert.Equal(3072, options.Embeddings.Dimensions);
        Assert.Equal("None", options.Embeddings.Provider);
    }

    [Fact]
    public void AddArcKnowledge_uses_disabled_embeddings_without_azure_config()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ArcKnowledge:DocumentIntelligenceEndpoint"] = "https://localhost-disabled.invalid/",
                ["ArcKnowledge:Embeddings:Provider"] = "None",
                ["ArcKnowledge:Embeddings:Dimensions"] = "3072"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(configuration);
        services.AddArcKnowledge(configuration);
        services.AddSingleton<IDocumentRetriever, NullRetriever>();

        using var provider = services.BuildServiceProvider();
        var embeddings = provider.GetRequiredService<IEmbeddingProvider>();
        Assert.IsType<DisabledEmbeddingProvider>(embeddings);
        Assert.False(embeddings.IsAvailable);
        Assert.Equal(3072, embeddings.Dimensions);
    }

    [Fact]
    public void Document_retriever_can_be_replaced_in_DI()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ArcKnowledge:DocumentIntelligenceEndpoint"] = "https://localhost-disabled.invalid/"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddArcKnowledge(configuration);
        var existing = services.Where(d => d.ServiceType == typeof(IDocumentRetriever)).ToList();
        foreach (var d in existing)
            services.Remove(d);
        services.AddSingleton<IDocumentRetriever, NullRetriever>();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<NullRetriever>(provider.GetRequiredService<IDocumentRetriever>());
    }

    [Fact]
    public void AddArcKnowledge_registers_demo_document_intelligence_without_endpoint()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IChequeRepository, MissingCheques>();
        services.AddSingleton<IEvidenceDocumentRepository, MissingEvidence>();
        services.AddArcKnowledge(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<DemoDocumentIntelligenceService>(provider.GetRequiredService<IDocumentIntelligenceService>());
        Assert.IsType<InMemoryDocumentExtractionStore>(provider.GetRequiredService<IDocumentExtractionStore>());
        Assert.NotNull(provider.GetRequiredService<IDocumentExtractionOrchestrator>());
    }

    [Fact]
    public void AddArcKnowledge_registers_azure_document_intelligence_when_endpoint_configured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ArcKnowledge:DocumentIntelligenceEndpoint"] = "https://localhost-disabled.invalid/"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddArcKnowledge(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<DocumentIntelligenceService>(provider.GetRequiredService<IDocumentIntelligenceService>());
    }

    [Fact]
    public void Demo_document_intelligence_can_replace_azure_registration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ArcKnowledge:DocumentIntelligenceEndpoint"] = "https://localhost-disabled.invalid/"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IChequeRepository, MissingCheques>();
        services.AddArcKnowledge(configuration);
        services.AddSingleton<IDocumentIntelligenceService, DemoDocumentIntelligenceService>();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<DemoDocumentIntelligenceService>(provider.GetRequiredService<IDocumentIntelligenceService>());
    }

    private sealed class MissingCheques : IChequeRepository
    {
        public Task<IReadOnlyList<ARC.Domain.Entities.SecurityCheque>> ListChequesAsync(ARC.Domain.ValueObjects.DealerUrn urn, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ARC.Domain.Entities.SecurityCheque>>([]);

        public Task<IReadOnlyList<ARC.Domain.Entities.ChequeReturnMemo>> ListReturnMemosAsync(ARC.Domain.ValueObjects.DealerUrn urn, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ARC.Domain.Entities.ChequeReturnMemo>>([]);
    }

    private sealed class MissingEvidence : IEvidenceDocumentRepository
    {
        public Task<ARC.Domain.Entities.EvidenceDocument> UploadAsync(
            ARC.Domain.ValueObjects.DealerUrn dealerUrn,
            ARC.Domain.Enums.DocumentType type,
            Stream content,
            string fileName,
            string contentType,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Stream> DownloadAsync(ARC.Domain.Entities.EvidenceDocument document, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task<bool> ExistsAsync(ARC.Domain.Entities.EvidenceDocument document, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task DeleteAsync(ARC.Domain.Entities.EvidenceDocument document, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class NullRetriever : IDocumentRetriever
    {
        public Task<IReadOnlyList<ARC.Knowledge.Provenance.EvidenceSource>> RetrieveAsync(
            DocumentRetrievalRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ARC.Knowledge.Provenance.EvidenceSource>>([]);
    }
}

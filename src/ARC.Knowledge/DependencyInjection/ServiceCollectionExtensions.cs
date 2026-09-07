using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Knowledge.Chunking;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Documents;
using ARC.Knowledge.Embeddings;
using ARC.Knowledge.Graph;
using ARC.Knowledge.Grounding;
using ARC.Knowledge.Ingestion;
using ARC.Knowledge.Retrieval;
using ARC.Knowledge.Vector;

namespace ARC.Knowledge.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArcKnowledge(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ArcKnowledgeOptions>(configuration.GetSection(ArcKnowledgeOptions.SectionName));
        services.AddSingleton<IDocumentExtractionStore, InMemoryDocumentExtractionStore>();
        services.AddSingleton<IDocumentExtractionOrchestrator, DocumentExtractionOrchestrator>();
        if (HasDocumentIntelligenceEndpoint(configuration))
            services.AddSingleton<IDocumentIntelligenceService, DocumentIntelligenceService>();
        else
            services.AddSingleton<IDocumentIntelligenceService, DemoDocumentIntelligenceService>();
        services.AddSingleton<IGraphTraversal, GraphTraversal>();
        services.AddSingleton<IDocumentRetriever, CosmosDocumentRetriever>();
        services.AddSingleton<IEmbeddingProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ArcKnowledgeOptions>>().Value.Embeddings;
            if (IsAzureOpenAI(options))
            {
                return new AzureOpenAIEmbeddingProvider(
                    sp.GetRequiredService<IOptions<ArcKnowledgeOptions>>(),
                    sp.GetRequiredService<ILogger<AzureOpenAIEmbeddingProvider>>());
            }

            return new DisabledEmbeddingProvider(options);
        });
        services.AddSingleton<IKnowledgeRetrievalService, KnowledgeRetrievalService>();
        services.AddSingleton<ICaseGraphContextProvider, CaseGraphContextProvider>();
        services.AddSingleton<IGroundingContextProvider, GroundingContextProvider>();

        // Phase 2: Ingestion pipeline
        services.AddSingleton<IDocumentChunker, PolicyClauseChunker>();
        services.AddSingleton<IDocumentChunker, TemplateChunker>();
        services.AddSingleton<IContentSanitizer, NoOpContentSanitizer>();
        services.AddSingleton<IIndexedDocumentStore, CosmosIndexedDocumentStore>();
        services.AddSingleton<IDocumentIngestionService, DocumentIngestionService>();

        return services;
    }

    private static bool IsAzureOpenAI(EmbeddingProviderOptions options)
        => string.Equals(options.Provider, "AzureOpenAI", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(options.Endpoint)
           && !string.IsNullOrWhiteSpace(options.Deployment);

    private static bool HasDocumentIntelligenceEndpoint(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(configuration[$"{ArcKnowledgeOptions.SectionName}:DocumentIntelligenceEndpoint"]);
}

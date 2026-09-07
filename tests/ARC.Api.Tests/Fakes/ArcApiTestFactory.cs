using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ARC.Data.Blob;
using ARC.Data.Cosmos;
using ARC.Data.Messaging;
using ARC.Data.Sql;
using ARC.Knowledge.Documents;
using ARC.Knowledge.Embeddings;
using ARC.Knowledge.Graph;
using ARC.Knowledge.Retrieval;

namespace ARC.Api.Tests.Fakes;

/// <summary>
/// Test factory that replaces Azure dependencies with test doubles for local MCP testing.
/// </summary>
public sealed class ArcApiTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ArcData:SqlConnectionString"] = "Server=localhost;Database=test;",
                ["ArcData:CosmosConnectionString"] = "AccountEndpoint=https://localhost:8081/;",
                ["ArcData:BlobConnectionString"] = "DefaultEndpointsProtocol=https;",
                ["ArcData:ServiceBusConnectionString"] = "Endpoint=sb://localhost/",
                ["ArcKnowledge:DocumentIntelligenceEndpoint"] = "https://localhost/",
                ["ArcKnowledge:Embeddings:Endpoint"] = "https://localhost/",
                ["ArcGuardrails:FailOpen"] = "true",
                ["ArcData:Odos:Session:CommtYear"] = "2026",
                ["ArcData:Odos:Session:CommtMonth"] = "07",
                ["ArcData:Odos:Session:UserId"] = "arc.test",
                ["ArcTools:FieldPersistence:UseInMemory"] = "true",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove Azure-dependent services
            services.RemoveAll<ISqlConnectionFactory>();
            services.RemoveAll<ICosmosClientFactory>();
            services.RemoveAll<IBlobStorageService>();
            services.RemoveAll<IServiceBusPublisher>();
            services.RemoveAll<IDocumentIntelligenceService>();
            services.RemoveAll<IEmbeddingProvider>();
            services.RemoveAll<IDocumentRetriever>();
            services.RemoveAll<IGraphTraversal>();
            services.RemoveAll<ARC.Knowledge.Ingestion.IIndexedDocumentStore>();
            services.RemoveAll<ARC.Knowledge.Ingestion.IDocumentIngestionService>();
            services.RemoveAll<IDealerRepository>();
            services.RemoveAll<IDealerMasterDetailReader>();
            services.RemoveAll<IRecoveryCaseRepository>();
            services.RemoveAll<IGateDecisionRepository>();
            services.RemoveAll<IWorkflowStateRepository>();

            // Add test fakes
            services.AddSingleton<ISqlConnectionFactory, FakeSqlConnectionFactory>();
            services.AddSingleton<IDealerRepository, FakeDealerRepository>();
            services.AddSingleton<FakeDealerMasterDetailReader>();
            services.AddSingleton<IDealerMasterDetailReader>(sp => sp.GetRequiredService<FakeDealerMasterDetailReader>());
            services.AddSingleton<FakeOdosOutstandingDetailReader>();
            services.AddSingleton<ARC.Data.Odos.IOdosOutstandingDetailReader>(sp => sp.GetRequiredService<FakeOdosOutstandingDetailReader>());
            services.AddSingleton<FakeA8SupervisionStore>();
            services.AddSingleton<IRecoveryCaseRepository>(sp => sp.GetRequiredService<FakeA8SupervisionStore>());
            services.AddSingleton<IGateDecisionRepository>(sp => sp.GetRequiredService<FakeA8SupervisionStore>());
            services.AddSingleton<IWorkflowStateRepository>(sp => sp.GetRequiredService<FakeA8SupervisionStore>());
            services.AddSingleton<ICosmosClientFactory, FakeCosmosClientFactory>();
            services.AddSingleton<ICosmosClientFactory, FakeCosmosClientFactory>();
            services.AddSingleton<IBlobStorageService, FakeBlobStorageService>();
            services.AddSingleton<IServiceBusPublisher, FakeServiceBusPublisher>();
            services.AddSingleton<IDocumentIntelligenceService, FakeDocumentIntelligenceService>();
            services.AddSingleton<IEmbeddingProvider, DisabledEmbeddingProvider>();
            services.AddSingleton<IDocumentRetriever, FakeDocumentRetriever>();
            services.AddSingleton<IGraphTraversal, FakeGraphTraversal>();
            services.AddSingleton<ARC.Knowledge.Ingestion.IIndexedDocumentStore, FakeIndexedDocumentStore>();
            services.AddSingleton<ARC.Knowledge.Ingestion.IDocumentIngestionService, FakeDocumentIngestionService>();
        });
    }
}

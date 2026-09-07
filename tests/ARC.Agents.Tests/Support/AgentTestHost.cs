using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ARC.Agents.DependencyInjection;
using ARC.Agents.Tests.Fakes;
using ARC.Data.Blob;
using ARC.Data.Cosmos;
using ARC.Data.Messaging;
using ARC.Data.Sql;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Documents;
using ARC.Knowledge.Grounding;
using ARC.Knowledge.Graph;
using ARC.Knowledge.Retrieval;
using ARC.Tools.DependencyInjection;
using ARC.Tools.Models;
using ARC.Guardrails;

namespace ARC.Agents.Tests.Support;

internal static class AgentTestHost
{
    public static (ServiceProvider Services, InMemoryHarness Store) Create(
        IKnowledgeRetrievalService? retrieval = null)
    {
        var store = new InMemoryHarness();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddSingleton(store);
        services.AddSingleton<IDealerRepository>(store);
        services.AddSingleton<ILedgerRepository>(store);
        services.AddSingleton<IChequeRepository>(store);
        services.AddSingleton<IGateDecisionRepository>(store);
        services.AddSingleton<ILegalCaseRepository>(store);
        services.AddSingleton<IRecoveryCaseRepository>(store);
        services.AddSingleton<InMemoryDealerIdentityMappingRepository>();
        services.AddSingleton<IDealerIdentityMappingRepository>(sp => sp.GetRequiredService<InMemoryDealerIdentityMappingRepository>());
        services.AddSingleton<IWorkflowStateRepository>(store);
        services.AddSingleton<IConversationStateRepository>(store);
        services.AddSingleton<IAuditRepository>(store);
        services.AddSingleton<IEvidenceDocumentRepository>(store);
        services.AddSingleton<IServiceBusPublisher>(store);
        services.AddSingleton<IDocumentExtractionStore, InMemoryDocumentExtractionStore>();
        services.AddSingleton<IDocumentIntelligenceService, DemoDocumentIntelligenceService>();
        services.AddSingleton<IDocumentExtractionOrchestrator, DocumentExtractionOrchestrator>();
        services.AddOptions<ArcKnowledgeOptions>();
        services.AddSingleton<IKnowledgeRetrievalService>(retrieval ?? new EmptyKnowledgeRetrievalService());
        services.AddSingleton<IGraphTraversal, EmptyGraphTraversal>();
        services.AddSingleton<ICaseGraphContextProvider, CaseGraphContextProvider>();
        services.AddSingleton<IGroundingContextProvider, GroundingContextProvider>();
        services.AddSingleton<IChatClient, EmptyChatClient>();
        services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddArcTools(configuration);
        services.AddArcGuardrails(configuration);
        services.PostConfigure<ArcToolsOptions>(o => o.VoicePtpConfirmBelow = 0.80m);
        services.AddArcAgents(configuration);

        return (services.BuildServiceProvider(), store);
    }
}

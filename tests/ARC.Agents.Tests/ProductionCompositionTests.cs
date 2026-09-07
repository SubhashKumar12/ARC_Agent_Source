using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ARC.Agents.A3NoticeDecisioning;
using ARC.Agents.A5DraftingVerification;
using ARC.Agents.A8SupervisoryInsight;
using ARC.Agents.DependencyInjection;
using ARC.Agents.Tests.Fakes;
using ARC.Agents.Workflows.Executors;
using ARC.Data.Blob;
using ARC.Data.Cosmos;
using ARC.Data.Messaging;
using ARC.Data.Sql;
using ARC.Knowledge.DependencyInjection;
using ARC.Knowledge.Documents;
using ARC.Knowledge.Graph;
using ARC.Knowledge.Grounding;
using ARC.Knowledge.Retrieval;
using ARC.Knowledge.Vector;
using ARC.Tools.DependencyInjection;
using ARC.Guardrails;

namespace ARC.Agents.Tests;

/// <summary>
/// Validates that the production DI composition can resolve all agent dependencies.
/// These tests catch missing registrations that unit tests might mask with test-specific DI.
/// </summary>
public sealed class ProductionCompositionTests
{
    [Fact]
    public void Production_composition_resolves_A3_agent()
    {
        // Arrange
        using var host = CreateProductionStyleHost();

        // Act & Assert - Should not throw
        var agent = host.GetRequiredService<NoticeDecisioningAgent>();
        Assert.NotNull(agent);
        Assert.NotNull(agent.Agent);
    }

    [Fact]
    public void Production_composition_resolves_A5_agent()
    {
        // Arrange
        using var host = CreateProductionStyleHost();

        // Act & Assert - Should not throw
        var agent = host.GetRequiredService<DraftingVerificationAgent>();
        Assert.NotNull(agent);
        Assert.NotNull(agent.Agent);
    }

    [Fact]
    public void Production_composition_resolves_A8_agent()
    {
        // Arrange
        using var host = CreateProductionStyleHost();

        // Act & Assert - Should not throw
        var agent = host.GetRequiredService<SupervisoryInsightAgent>();
        Assert.NotNull(agent);
        Assert.NotNull(agent.Agent);
    }

    [Fact]
    public void Production_composition_resolves_workflow_executors()
    {
        // Arrange
        using var host = CreateProductionStyleHost();

        // Act & Assert - Should not throw
        var executors = host.GetRequiredService<ArcWorkflowExecutors>();
        Assert.NotNull(executors);
    }

    [Fact]
    public void Production_composition_resolves_grounding_providers()
    {
        // Arrange
        using var host = CreateProductionStyleHost();

        // Act & Assert - Should not throw
        var groundingProvider = host.GetRequiredService<IGroundingContextProvider>();
        Assert.NotNull(groundingProvider);
        Assert.IsType<GroundingContextProvider>(groundingProvider);

        var graphProvider = host.GetRequiredService<ICaseGraphContextProvider>();
        Assert.NotNull(graphProvider);
        Assert.IsType<CaseGraphContextProvider>(graphProvider);
    }

    /// <summary>
    /// Creates a service provider using production-style DI registration paths.
    /// Uses in-memory/fake implementations for external dependencies (SQL, Cosmos, Azure)
    /// to avoid requiring live infrastructure, but follows the same registration order
    /// and extension methods as production hosts (API, Functions, CLI).
    /// </summary>
    private static ServiceProvider CreateProductionStyleHost()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        // In-memory/fake implementations for infrastructure
        services.AddSingleton<InMemoryHarness>();
        services.AddSingleton<IDealerRepository>(sp => sp.GetRequiredService<InMemoryHarness>());
        services.AddSingleton<ILedgerRepository>(sp => sp.GetRequiredService<InMemoryHarness>());
        services.AddSingleton<IChequeRepository>(sp => sp.GetRequiredService<InMemoryHarness>());
        services.AddSingleton<IGateDecisionRepository>(sp => sp.GetRequiredService<InMemoryHarness>());
        services.AddSingleton<ILegalCaseRepository>(sp => sp.GetRequiredService<InMemoryHarness>());
        services.AddSingleton<IRecoveryCaseRepository>(sp => sp.GetRequiredService<InMemoryHarness>());
        services.AddSingleton<InMemoryDealerIdentityMappingRepository>();
        services.AddSingleton<IDealerIdentityMappingRepository>(sp => sp.GetRequiredService<InMemoryDealerIdentityMappingRepository>());
        services.AddSingleton<IWorkflowStateRepository>(sp => sp.GetRequiredService<InMemoryHarness>());
        services.AddSingleton<IConversationStateRepository>(sp => sp.GetRequiredService<InMemoryHarness>());
        services.AddSingleton<IAuditRepository>(sp => sp.GetRequiredService<InMemoryHarness>());
        services.AddSingleton<IEvidenceDocumentRepository>(sp => sp.GetRequiredService<InMemoryHarness>());
        services.AddSingleton<IServiceBusPublisher>(sp => sp.GetRequiredService<InMemoryHarness>());
        services.AddSingleton<IChatClient, EmptyChatClient>();
        services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Production-style registration order (same as API, Functions, CLI)
        // CRITICAL: AddArcKnowledge MUST be called before AddArcAgents
        // Provide fake Cosmos client factory (AddArcData registers this in production)
        services.AddSingleton<ICosmosClientFactory>(sp => new FakeCosmosClientFactory());

        services.AddArcKnowledge(configuration);
        services.AddArcGuardrails(configuration);

        // Override with fakes (same pattern as CLI for non-Cosmos services)
        services.AddSingleton<IKnowledgeRetrievalService, EmptyKnowledgeRetrievalService>();
        services.AddSingleton<IGraphTraversal, EmptyGraphTraversal>();
        services.AddSingleton<IDocumentIntelligenceService, DemoDocumentIntelligenceService>();

        services.AddArcTools(configuration);
        services.AddArcAgents(configuration);

        return services.BuildServiceProvider();
    }

    private sealed class FakeCosmosClientFactory : ICosmosClientFactory
    {
        private Microsoft.Azure.Cosmos.Container FakeContainer 
            => throw new NotImplementedException("Fake Cosmos client for composition testing only");

        public Microsoft.Azure.Cosmos.Container Checkpoints => FakeContainer;
        public Microsoft.Azure.Cosmos.Container CycleState => FakeContainer;
        public Microsoft.Azure.Cosmos.Container Audit => FakeContainer;
        public Microsoft.Azure.Cosmos.Container Conversation => FakeContainer;
        public Microsoft.Azure.Cosmos.Container Documents => FakeContainer;
    }
}

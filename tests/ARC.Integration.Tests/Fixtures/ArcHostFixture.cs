using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ARC.Agents.DependencyInjection;
using ARC.Agents.Workflows.Outbound;
using ARC.Data.Blob;
using ARC.Data.Configuration;
using ARC.Data.DependencyInjection;
using ARC.Data.Messaging;
using ARC.Data.Serialization;
using ARC.Host.Functions;
using ARC.Host.Functions.Checkpointing;
using ARC.Host.Functions.Runtime;
using ARC.Knowledge.DependencyInjection;
using ARC.Tools.DependencyInjection;
using ARC.Guardrails;

namespace ARC.Integration.Tests.Fixtures;

/// <summary>
/// Builds a Host DI root equivalent to ARC.Host.Functions for AC#2,
/// using production <see cref="CosmosJsonCheckpointStore"/>.
/// </summary>
public sealed class ArcHostFixture
{
    private readonly SqlFixture _sql;
    private readonly CosmosFixture _cosmos;

    public ArcHostFixture(SqlFixture sql, CosmosFixture cosmos)
    {
        _sql = sql;
        _cosmos = cosmos;
    }

    public IHost CreateHost(RecordingOutboundGate outbound)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Configuration.AddJsonFile("appsettings.Integration.json", optional: false);
        builder.Configuration.AddEnvironmentVariables();

        var inMemory = new Dictionary<string, string?>
        {
            ["ArcHost:DefaultRunMode"] = "Shadow",
            ["ArcData:Sql:ConnectionString"] = _sql.ConnectionString,
            ["ArcData:Sql:UseManagedIdentity"] = "false",
            ["ArcData:Cosmos:ConnectionString"] = _cosmos.ConnectionString,
            ["ArcData:Cosmos:UseManagedIdentity"] = "false",
            ["ArcData:Cosmos:DatabaseId"] = _cosmos.DatabaseId,
            ["ArcData:Cosmos:CheckpointsContainer"] = "checkpoints",
            ["ArcData:Cosmos:CycleStateContainer"] = "cycleState",
            ["ArcData:Cosmos:AuditContainer"] = "auditEvents",
            ["ArcData:Cosmos:ConversationContainer"] = "conversationState",
            ["ArcData:Cosmos:DocumentsContainer"] = "documents",
            // Blob/SB overridden by NoOp registrations; placeholders avoid accidental real clients.
            ["ArcData:Blob:ConnectionString"] = "UseDevelopmentStorage=true",
            ["ArcData:Blob:UseManagedIdentity"] = "false",
            ["ArcKnowledge:DocumentIntelligenceEndpoint"] = "https://localhost-disabled.invalid/"
        };
        builder.Configuration.AddInMemoryCollection(inMemory);

        builder.Services.Configure<ArcHostOptions>(builder.Configuration.GetSection(ArcHostOptions.SectionName));
        builder.Services.AddArcData(builder.Configuration);
        builder.Services.AddArcKnowledge(builder.Configuration);
        builder.Services.AddArcGuardrails(builder.Configuration);
        builder.Services.AddArcTools(builder.Configuration);
        builder.Services.AddSingleton<IChatClient, StubChatClient>();
        builder.Services.AddArcAgents(builder.Configuration);

        // Override Azure peripherals only — CosmosJsonCheckpointStore remains production.
        ReplaceSingleton<IServiceBusPublisher, NoOpServiceBusPublisher>(builder.Services);
        ReplaceSingleton<IBlobStorageService, NoOpBlobStorageService>(builder.Services);
        foreach (var descriptor in builder.Services.Where(d => d.ServiceType == typeof(IOutboundGate)).ToList())
            builder.Services.Remove(descriptor);
        builder.Services.AddSingleton<IOutboundGate>(outbound);

        builder.Services.AddSingleton<ICheckpointStore<JsonElement>, CosmosJsonCheckpointStore>();
        builder.Services.AddSingleton(sp => CheckpointManager.CreateJson(
            sp.GetRequiredService<ICheckpointStore<JsonElement>>(),
            new JsonSerializerOptions(ArcJson.Options)));
        builder.Services.AddSingleton<DealerWorkflowRunner>();

        return builder.Build();
    }

    private static void ReplaceSingleton<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        var existing = services.Where(d => d.ServiceType == typeof(TService)).ToList();
        foreach (var descriptor in existing)
            services.Remove(descriptor);
        services.AddSingleton<TService, TImplementation>();
    }
}

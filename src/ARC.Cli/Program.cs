using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ARC.Agents.DependencyInjection;
using ARC.Agents.Workflows.Outbound;
using ARC.Cli.Commands;
using ARC.Cli.Fakes;
using ARC.Cli.Runtime;
using ARC.Cli.Scenarios;
using ARC.Data.Blob;
using ARC.Data.Cosmos;
using ARC.Data.Messaging;
using ARC.Data.Odos;
using ARC.Data.Serialization;
using ARC.Data.Sql;
using ARC.Knowledge.DependencyInjection;
using ARC.Knowledge.Graph;
using ARC.Knowledge.Ingestion;
using ARC.Knowledge.Retrieval;
using ARC.Tools.DependencyInjection;
using ARC.Tools.Models;
using ARC.Guardrails;

// Check if ingestion command requested (not part of S1-S9)
if (args.Length > 0 && string.Equals(args[0], "ingest", StringComparison.OrdinalIgnoreCase))
{
    return await RunIngestionCommandAsync(args);
}

if (args.Length > 0 && string.Equals(args[0], "synthetic", StringComparison.OrdinalIgnoreCase))
{
    return await new SyntheticDatasetCommand().RunAsync(CancellationToken.None);
}

// Phase 11D read-only ODOS SQL commands. No writes, no workflow state.
if (args.Length > 0 && string.Equals(args[0], "sql-check", StringComparison.OrdinalIgnoreCase))
{
    return await OdosSqlCommands.RunSqlCheckAsync(args, CancellationToken.None);
}

if (args.Length > 0 && string.Equals(args[0], "odos-dry-run", StringComparison.OrdinalIgnoreCase))
{
    return await OdosSqlCommands.RunOdosDryRunAsync(args, CancellationToken.None);
}

if (args.Length > 0 && string.Equals(args[0], "readiness", StringComparison.OrdinalIgnoreCase))
{
    return await ReadinessCommands.RunReadinessAsync(args, CancellationToken.None);
}

if (args.Length > 0 && string.Equals(args[0], "azure-preflight", StringComparison.OrdinalIgnoreCase))
{
    return await ReadinessCommands.RunAzurePreflightAsync(args, CancellationToken.None);
}

var ids = ParseScenarioIds(args);
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ArcGuardrails:FailOpen"] = "false",
    ["ArcGuardrails:Backend"] = "Local",
    ["ArcTools:FieldPersistence:UseInMemory"] = "true"
});
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services.AddSingleton<InMemoryArcStore>();
builder.Services.AddSingleton<IDealerRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<ILedgerRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IChequeRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IGateDecisionRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<ILegalCaseRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IRecoveryCaseRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<InMemoryDealerIdentityMappingRepository>();
builder.Services.AddSingleton<IDealerIdentityMappingRepository>(sp => sp.GetRequiredService<InMemoryDealerIdentityMappingRepository>());
builder.Services.AddSingleton<IWorkflowStateRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IConversationStateRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IAuditRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IEvidenceDocumentRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IServiceBusPublisher>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IDealerMasterDetailReader, CliUnusedDealerMasterDetailReader>();
builder.Services.AddSingleton<IOdosOutstandingDetailReader, CliUnusedOdosOutstandingDetailReader>();

// Provide Cosmos client dependencies required by AddArcKnowledge (but not actually used in S1-S9)
builder.Services.AddSingleton<Microsoft.Azure.Cosmos.CosmosClient>(sp =>
{
    throw new InvalidOperationException("S1-S9 scenarios do not use Cosmos");
});
builder.Services.AddSingleton<ICosmosClientFactory>(sp =>
{
    throw new InvalidOperationException("S1-S9 scenarios do not use Cosmos");
});

// Register full ARC Knowledge layer (includes grounding providers required by A3/A5/A8)
builder.Services.AddArcKnowledge(builder.Configuration);
builder.Services.AddArcGuardrails(builder.Configuration);

// CLI Shadow: never call Azure Document Intelligence
builder.Services.AddSingleton<ARC.Knowledge.Documents.IDocumentIntelligenceService, ARC.Knowledge.Documents.DemoDocumentIntelligenceService>();

// For S1-S9: Replace Cosmos document store with factory providing databaseId
var storeDescriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(IIndexedDocumentStore));
if (storeDescriptor != null)
{
    builder.Services.Remove(storeDescriptor);
    builder.Services.AddSingleton<IIndexedDocumentStore>(sp =>
    {
        var cosmosClient = sp.GetRequiredService<Microsoft.Azure.Cosmos.CosmosClient>();
        return new CosmosIndexedDocumentStore(cosmosClient, "arc");
    });
}

// For S1-S9 scenarios: override with fake knowledge services (keep grounding providers)
builder.Services.AddSingleton<IKnowledgeRetrievalService, EmptyKnowledgeRetrievalService>();
builder.Services.AddSingleton<IGraphTraversal, EmptyGraphTraversal>();

builder.Services.AddSingleton<IChatClient, ShadowNarrationChatClient>();
builder.Services.AddSingleton<MemoryJsonCheckpointStore>();
builder.Services.AddSingleton<ICheckpointStore<JsonElement>>(sp => sp.GetRequiredService<MemoryJsonCheckpointStore>());
builder.Services.AddSingleton(sp => CheckpointManager.CreateJson(
    sp.GetRequiredService<ICheckpointStore<JsonElement>>(),
    new JsonSerializerOptions(ArcJson.Options)));

builder.Services.AddArcTools(builder.Configuration);
builder.Services.PostConfigure<ArcToolsOptions>(options =>
{
    options.VoicePtpConfirmBelow = 0.80m;
});
// CLI Shadow: never call Azure Speech or a microphone
builder.Services.AddSingleton<ARC.Tools.Speech.ISpeechTranscriptionService, ARC.Tools.Speech.DemoSpeechTranscriptionService>();
builder.Services.AddArcAgents(builder.Configuration);
builder.Services.AddSingleton<CliOutboundRecorder>();
builder.Services.AddSingleton<IOutboundGate>(sp => sp.GetRequiredService<CliOutboundRecorder>());
builder.Services.AddSingleton<CliWorkflowDriver>();
builder.Services.AddSingleton<ScenarioRunner>();

using var host = builder.Build();
var runner = host.Services.GetRequiredService<ScenarioRunner>();
var failed = 0;

Console.WriteLine("ARC CLI — local Shadow scenario runner (S1–S9). No Azure, no Live outbound.");
Console.WriteLine();

foreach (var id in ids)
{
    Console.WriteLine($"=== {id} ===");
    try
    {
        var outcome = await runner.RunAsync(id, CancellationToken.None);
        var mark = outcome.Passed ? "PASS" : "FAIL";
        if (!outcome.Passed)
            failed++;
        Console.WriteLine($"{mark}  {outcome.Summary}");
        foreach (var check in outcome.Checks)
            Console.WriteLine($"  {(check.Pass ? "ok" : "x ")} {check.Detail}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL  {id} threw:");
        Console.WriteLine(ex);
    }

    Console.WriteLine();
}

Console.WriteLine(failed == 0
    ? $"All {ids.Count} scenario(s) passed. Outbound remains Shadow."
    : $"{failed} of {ids.Count} scenario(s) failed.");

return failed == 0 ? 0 : 1;

static async Task<int> RunIngestionCommandAsync(string[] arguments)
{
    var builder = Host.CreateApplicationBuilder(arguments);
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    // Register full knowledge layer for ingestion (not fake)
    builder.Services.AddArcKnowledge(builder.Configuration);
    builder.Services.AddArcGuardrails(builder.Configuration);
    builder.Services.AddSingleton<IngestionCommand>();

    using var host = builder.Build();
    var command = host.Services.GetRequiredService<IngestionCommand>();

    try
    {
        return await command.RunAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        Console.WriteLine("Ingestion failed:");
        Console.WriteLine(ex.Message);
        return 1;
    }
}

static IReadOnlyList<string> ParseScenarioIds(string[] arguments)
{
    if (arguments.Length == 0 || string.Equals(arguments[0], "all", StringComparison.OrdinalIgnoreCase))
        return ["S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8", "S9"];

    return arguments
        .Select(a => a.Trim().ToUpperInvariant())
        .Where(a => a.Length > 0)
        .ToList();
}

using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.AI;
using ARC.Agents.DependencyInjection;
using ARC.Agents.Workflows.Outbound;
using ARC.Data.Blob;
using ARC.Data.Cosmos;
using ARC.Data.Messaging;
using ARC.Data.Odos;
using ARC.Data.Serialization;
using ARC.Data.Sql;
using ARC.Knowledge.DependencyInjection;
using ARC.Knowledge.Graph;
using ARC.Knowledge.Retrieval;
using ARC.Tools.DependencyInjection;
using ARC.Tools.Models;
using ARC.Web.Services;
using ARC.Guardrails;

var builder = WebApplication.CreateBuilder(args);

// DEMO MODE CONFIGURATION
var demoMode = builder.Configuration.GetValue<bool>("DemoMode");
var shadowMode = builder.Configuration.GetValue<bool>("ShadowMode");

if (!demoMode || !shadowMode)
{
    throw new InvalidOperationException("ARC.Web requires DemoMode=true and ShadowMode=true. This is a local demo UI only.");
}

// In-memory demo infrastructure (reuses CLI pattern)
builder.Services.AddSingleton<InMemoryArcStore>();
builder.Services.AddSingleton<IDealerRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<ILedgerRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IChequeRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IGateDecisionRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<ILegalCaseRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IRecoveryCaseRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IWorkflowStateRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IConversationStateRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IAuditRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IEvidenceDocumentRepository>(sp => sp.GetRequiredService<InMemoryArcStore>());
builder.Services.AddSingleton<IServiceBusPublisher>(sp => sp.GetRequiredService<InMemoryArcStore>());

// Identity mapping (use existing from ARC.Data.Sql)
builder.Services.AddSingleton<ARC.Data.Sql.InMemoryDealerIdentityMappingRepository>();
builder.Services.AddSingleton<IDealerIdentityMappingRepository>(sp => sp.GetRequiredService<ARC.Data.Sql.InMemoryDealerIdentityMappingRepository>());

// Provide Cosmos client dependencies required by AddArcKnowledge (but not actually used in demo)
builder.Services.AddSingleton<Microsoft.Azure.Cosmos.CosmosClient>(sp =>
{
    throw new InvalidOperationException("Demo mode does not use Cosmos");
});
builder.Services.AddSingleton<ICosmosClientFactory>(sp =>
{
    throw new InvalidOperationException("Demo mode does not use Cosmos");
});

// Register full ARC Knowledge layer (includes grounding providers required by A3/A5/A8)
builder.Services.AddArcKnowledge(builder.Configuration);
builder.Services.AddArcGuardrails(builder.Configuration);

// Local demo: never call Azure Document Intelligence
builder.Services.AddSingleton<ARC.Knowledge.Documents.IDocumentIntelligenceService, ARC.Knowledge.Documents.DemoDocumentIntelligenceService>();

// For demo: Replace Cosmos document store with factory providing databaseId (matches CLI pattern)
var storeDescriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(ARC.Knowledge.Ingestion.IIndexedDocumentStore));
if (storeDescriptor != null)
{
    builder.Services.Remove(storeDescriptor);
    builder.Services.AddSingleton<ARC.Knowledge.Ingestion.IIndexedDocumentStore>(sp =>
    {
        var cosmosClient = sp.GetRequiredService<Microsoft.Azure.Cosmos.CosmosClient>();
        return new ARC.Knowledge.Ingestion.CosmosIndexedDocumentStore(cosmosClient, "arc-demo");
    });
}

// For demo: override with empty knowledge services (keep grounding providers)
builder.Services.AddSingleton<IKnowledgeRetrievalService, EmptyKnowledgeRetrievalService>();
builder.Services.AddSingleton<IGraphTraversal, EmptyGraphTraversal>();

// Shadow narration client (no real LLM calls)
builder.Services.AddSingleton<ShadowNarrationChatClient>();
builder.Services.AddSingleton<IChatClient>(sp => sp.GetRequiredService<ShadowNarrationChatClient>());

// MAF checkpoint store
builder.Services.AddSingleton<MemoryJsonCheckpointStore>();
builder.Services.AddSingleton<ICheckpointStore<JsonElement>>(sp => sp.GetRequiredService<MemoryJsonCheckpointStore>());
builder.Services.AddSingleton(sp => CheckpointManager.CreateJson(
    sp.GetRequiredService<ICheckpointStore<JsonElement>>(),
    new JsonSerializerOptions(ArcJson.Options)));

// ARC tools and agents
builder.Services.AddSingleton<IDealerMasterDetailReader, DemoDealerMasterDetailReader>();
builder.Services.AddSingleton<IOdosOutstandingDetailReader, WebUnusedOutstandingDetailReader>();
builder.Services.AddArcTools(builder.Configuration);
builder.Services.AddSingleton<ARC.Tools.Speech.ISpeechTranscriptionService, ARC.Tools.Speech.DemoSpeechTranscriptionService>();
builder.Services.AddArcAgents(builder.Configuration);

// Shadow outbound gate
builder.Services.AddSingleton<WebDemoOutboundGate>();
builder.Services.AddSingleton<IOutboundGate>(sp => sp.GetRequiredService<WebDemoOutboundGate>());

// Demo data seeder
builder.Services.AddSingleton<DemoDataSeeder>();

builder.Services.Configure<ArcWebBusinessChatOptions>(
    builder.Configuration.GetSection(ArcWebBusinessChatOptions.SectionName));
builder.Services.AddHttpClient<IBusinessChatApiClient, BusinessChatApiClient>();

// Razor Pages
builder.Services.AddRazorPages();

// Session state for demo
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

var chatOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ArcWebBusinessChatOptions>>().Value;
_ = Task.Run(async () =>
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var healthUrl = $"{chatOptions.ApiBaseUrl.TrimEnd('/')}/health";
        var response = await client.GetAsync(healthUrl);
        if (response.IsSuccessStatusCode)
            app.Logger.LogInformation("ARC.Api reachable at {ApiBaseUrl}", chatOptions.ApiBaseUrl);
        else
            app.Logger.LogWarning("ARC.Api health check failed ({StatusCode}) at {ApiBaseUrl}. Start ARC.Api before using Business Chat.", (int)response.StatusCode, chatOptions.ApiBaseUrl);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "ARC.Api is not reachable at {ApiBaseUrl}. Start ARC.Api with: dotnet run --project src/ARC.Api -c Release --launch-profile http", chatOptions.ApiBaseUrl);
    }
});

app.Run();

// Make Program class accessible for testing
public partial class Program { }

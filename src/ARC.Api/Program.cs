using System.Text.Json.Serialization;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ARC.Agents.DependencyInjection;
using ARC.Api.Auth;
using ARC.Api.Chat;
using ARC.Api.Configuration;
using ARC.Api.Middleware;
using ARC.Api.Services;
using ARC.Data.Configuration;
using ARC.Data.DependencyInjection;
using ARC.Knowledge.DependencyInjection;
using ARC.Knowledge.Ingestion;
using ARC.Tools.DependencyInjection;
using ARC.Guardrails;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<OdosSqlConnectionOptions>(builder.Configuration.GetSection(OdosSqlConnectionOptions.SectionName));
builder.Services.Configure<ArcApiOptions>(builder.Configuration.GetSection(ArcApiOptions.SectionName));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ArcExceptionHandler>();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var apiOptions = builder.Configuration.GetSection(ArcApiOptions.SectionName).Get<ArcApiOptions>() ?? new();
if (!string.IsNullOrWhiteSpace(apiOptions.JwtAuthority))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = apiOptions.JwtAuthority;
            options.Audience = apiOptions.JwtAudience;
            options.MapInboundClaims = false;
        });
    builder.Services.AddAuthorization();
}

builder.Services.AddArcData(builder.Configuration);

builder.Services.AddSingleton<CosmosClient>(sp =>
{
    var cosmos = sp.GetRequiredService<IOptions<ArcDataOptions>>().Value.Cosmos;
    if (!string.IsNullOrWhiteSpace(cosmos.ConnectionString))
        return new CosmosClient(cosmos.ConnectionString);
    if (!string.IsNullOrWhiteSpace(cosmos.AccountEndpoint) && cosmos.UseManagedIdentity)
        return new CosmosClient(cosmos.AccountEndpoint, new DefaultAzureCredential());
    throw new InvalidOperationException("Configure ArcData:Cosmos AccountEndpoint + UseManagedIdentity, or ConnectionString.");
});

builder.Services.AddArcKnowledge(builder.Configuration);

var indexedDocumentStore = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(IIndexedDocumentStore));
if (indexedDocumentStore is not null)
{
    builder.Services.Remove(indexedDocumentStore);
    builder.Services.AddSingleton<IIndexedDocumentStore>(sp =>
    {
        var cosmosClient = sp.GetRequiredService<CosmosClient>();
        var databaseId = sp.GetRequiredService<IOptions<ArcDataOptions>>().Value.Cosmos.DatabaseId;
        return new CosmosIndexedDocumentStore(cosmosClient, databaseId);
    });
}
builder.Services.AddArcGuardrails(builder.Configuration);
builder.Services.AddArcTools(builder.Configuration);
builder.Services.AddSingleton<DeterministicSemanticCapabilityResolver>();
builder.Services.AddSingleton<LlmSemanticCapabilityResolver>();
builder.Services.AddSingleton<ISemanticCapabilityResolver, CompositeSemanticCapabilityResolver>();
builder.Services.Configure<BusinessChatSemanticOptions>(
    builder.Configuration.GetSection(BusinessChatSemanticOptions.SectionName));
builder.Services.AddSingleton<BusinessChatQueryExecutor>();
builder.Services.AddSingleton<BusinessChatService>();
builder.Services.AddSingleton<IBusinessChatConversationStore, InMemoryBusinessChatConversationStore>();
builder.Services.AddSingleton<IChatClient, ShadowNarrationChatClient>();
builder.Services.AddArcAgents(builder.Configuration);

// MCP V1: Thin adapter over existing ARC tools (8 read-only decision-support capabilities)
builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly();

var app = builder.Build();
app.UseExceptionHandler();
if (!string.IsNullOrWhiteSpace(apiOptions.JwtAuthority))
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseMiddleware<ArcActorMiddleware>();
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }));
app.MapControllers();

// MCP endpoint - authentication and authorization enforced via ArcActorMiddleware
app.MapMcp("/mcp");

app.Run();

// Make Program accessible for integration testing
public partial class Program { }

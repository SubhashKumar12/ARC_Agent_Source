using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Net.Http.Json;
using ARC.Web.Services;
using ARC.Data.Sql;
using ARC.Data.Cosmos;
using ARC.Agents.Workflows.Outbound;
using ARC.Knowledge.Documents;
using ARC.Web.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ARC.Web.Tests;

// Custom factory that disables service validation for tests
public sealed class ArcWebTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.AddOptions<ServiceProviderOptions>().Configure(options =>
            {
                options.ValidateOnBuild = false;
                options.ValidateScopes = false;
            });
            services.RemoveAll<IBusinessChatApiClient>();
            services.AddSingleton<IBusinessChatApiClient, FakeBusinessChatApiClient>();
        });
    }
}

public sealed class WebDemoTests : IClassFixture<ArcWebTestFactory>
{
    private readonly ArcWebTestFactory _factory;

    public WebDemoTests(ArcWebTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Application_Starts_Successfully()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Main_Page_Contains_Shadow_Badge()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();
        
        Assert.Contains("SHADOW", content);
    }

    [Fact]
    public async Task Dev_Demo_Page_Contains_All_Scenarios()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/DevDemo");
        var content = await response.Content.ReadAsStringAsync();
        
        for (int i = 1; i <= 9; i++)
        {
            Assert.Contains($"S{i}", content);
        }
    }

    [Fact]
    public void Demo_Infrastructure_Uses_InMemoryArcStore()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetService<InMemoryArcStore>();
        
        Assert.NotNull(store);
    }

    [Fact]
    public void Demo_Infrastructure_Implements_All_Required_Repositories()
    {
        using var scope = _factory.Services.CreateScope();
        
        // Verify all repository interfaces are satisfied
        Assert.NotNull(scope.ServiceProvider.GetService<IDealerRepository>());
        Assert.NotNull(scope.ServiceProvider.GetService<ILedgerRepository>());
        Assert.NotNull(scope.ServiceProvider.GetService<IChequeRepository>());
        Assert.NotNull(scope.ServiceProvider.GetService<IGateDecisionRepository>());
        Assert.NotNull(scope.ServiceProvider.GetService<ILegalCaseRepository>());
        Assert.NotNull(scope.ServiceProvider.GetService<IRecoveryCaseRepository>());
        Assert.NotNull(scope.ServiceProvider.GetService<IWorkflowStateRepository>());
        Assert.NotNull(scope.ServiceProvider.GetService<IConversationStateRepository>());
        Assert.NotNull(scope.ServiceProvider.GetService<IAuditRepository>());
    }

    [Fact]
    public void Demo_Infrastructure_Uses_Shadow_Outbound_Gate()
    {
        using var scope = _factory.Services.CreateScope();
        var outbound = scope.ServiceProvider.GetService<IOutboundGate>();
        
        Assert.NotNull(outbound);
        Assert.IsType<WebDemoOutboundGate>(outbound);
    }

    [Fact]
    public void Demo_Infrastructure_Uses_Shadow_Narration()
    {
        using var scope = _factory.Services.CreateScope();
        var chatClient = scope.ServiceProvider.GetService<Microsoft.Extensions.AI.IChatClient>();
        
        Assert.NotNull(chatClient);
        Assert.IsType<ShadowNarrationChatClient>(chatClient);
    }

    [Fact]
    public void Demo_Infrastructure_Has_Data_Seeder()
    {
        using var scope = _factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetService<DemoDataSeeder>();
        
        Assert.NotNull(seeder);
    }

    [Fact]
    public async Task Page_Refresh_Does_Not_Require_Authentication()
    {
        var client = _factory.CreateClient();
        var firstResponse = await client.GetAsync("/");
        firstResponse.EnsureSuccessStatusCode();
        
        var secondResponse = await client.GetAsync("/");
        secondResponse.EnsureSuccessStatusCode();
        
        // Both succeed without authentication
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Shadow_Actions_Endpoint_Returns_Success()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Index?handler=ShadowActions");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public void Demo_Mode_Does_Not_Require_Real_Azure()
    {
        using var scope = _factory.Services.CreateScope();
        
        // Verify demo infrastructure is in place
        // Azure services are registered but will throw if actually used
        Assert.NotNull(scope.ServiceProvider.GetService<InMemoryArcStore>());
        Assert.NotNull(scope.ServiceProvider.GetService<DemoDataSeeder>());
        Assert.NotNull(scope.ServiceProvider.GetService<WebDemoOutboundGate>());
        Assert.NotNull(scope.ServiceProvider.GetService<ShadowNarrationChatClient>());
        Assert.IsType<DemoDocumentIntelligenceService>(
            scope.ServiceProvider.GetRequiredService<IDocumentIntelligenceService>());
        Assert.IsType<ARC.Tools.Speech.DemoSpeechTranscriptionService>(
            scope.ServiceProvider.GetRequiredService<ARC.Tools.Speech.ISpeechTranscriptionService>());
    }
}

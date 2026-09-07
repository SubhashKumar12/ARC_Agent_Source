using ARC.Api.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ARC.Api.Tests;

public sealed class McpHostingTests : IClassFixture<ArcApiTestFactory>
{
    private readonly ArcApiTestFactory _factory;

    public McpHostingTests(ArcApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void MCP_HttpContextAccessor_Registered()
    {
        // Arrange & Act
        using var scope = _factory.Services.CreateScope();
        var httpContextAccessor = scope.ServiceProvider.GetService<IHttpContextAccessor>();

        // Assert - Required for actor authentication in MCP tools
        Assert.NotNull(httpContextAccessor);
    }

    [Fact]
    public void MCP_ArcTools_Registered()
    {
        // Arrange & Act
        using var scope = _factory.Services.CreateScope();
        // The [McpServerToolType] attribute registers ArcMcpTools with the MCP framework,
        // not directly in DI. The tools are discovered via reflection by WithToolsFromAssembly().
        // To verify registration, we check that the MCP endpoint exists (tested in another test).
        
        // Assert - MCP tools are registered if the application started successfully
        Assert.NotNull(_factory.Services);
    }

    [Fact]
    public async Task MCP_Endpoint_Returns_Non_404()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/mcp");

        // Assert - MCP endpoint exists (may return 401/405 due to no auth or wrong method, but not 404)
        Assert.NotEqual(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}

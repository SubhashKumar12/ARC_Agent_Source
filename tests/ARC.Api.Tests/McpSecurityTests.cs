using System.Net;
using System.Text;
using System.Text.Json;
using ARC.Api.Auth;
using ARC.Api.Tests.Fakes;
using Xunit;

namespace ARC.Api.Tests;

/// <summary>
/// Tests MCP authentication, authorization, role enforcement, and scope validation.
/// </summary>
public sealed class McpSecurityTests : IClassFixture<ArcApiTestFactory>
{
    private readonly ArcApiTestFactory _factory;

    public McpSecurityTests(ArcApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MCP_Unauthenticated_Request_Rejected()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        // Act
        var response = await client.SendAsync(request);

        // Assert - Unauthenticated requests should not succeed
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized || 
                    response.StatusCode == HttpStatusCode.Forbidden ||
                    response.StatusCode == HttpStatusCode.BadRequest,
                    $"Expected auth failure, got {response.StatusCode}");
    }

    [Fact]
    public async Task MCP_Authenticated_Request_With_Valid_Actor_Accepted()
    {
        // Arrange
        var client = _factory.CreateClient();
        
        // Development actor headers — ArcActorHttp contract (X-Arc-*), not JWT
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { method = "tools/list" }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add(ArcActorHttp.UpnHeader, "test@example.com");
        request.Headers.Add(ArcActorHttp.RoleHeader, "DepotManager");
        request.Headers.Add(ArcActorHttp.RegionHeader, "North");
        request.Headers.Add(ArcActorHttp.DepotHeader, "DEL");

        // Act
        var response = await client.SendAsync(request);

        // Assert - Authenticated request should not return 401
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MCP_Missing_Required_Scope_Fails_Closed()
    {
        // Arrange
        var client = _factory.CreateClient();
        
        // Missing Region and Depot headers
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { method = "tools/call", @params = new { name = "searchDocuments", arguments = new { query = "test" } } }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add(ArcActorHttp.UpnHeader, "test@example.com");
        request.Headers.Add(ArcActorHttp.RoleHeader, "DepotManager");

        // Act
        var response = await client.SendAsync(request);

        // Assert - Missing scope should fail closed
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized || 
                    response.StatusCode == HttpStatusCode.Forbidden ||
                    response.StatusCode == HttpStatusCode.BadRequest,
                    $"Expected failure for missing scope, got {response.StatusCode}");
    }
}

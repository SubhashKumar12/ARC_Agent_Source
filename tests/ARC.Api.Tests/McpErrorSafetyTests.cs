using System.Net;
using System.Text;
using System.Text.Json;
using ARC.Api.Auth;
using ARC.Api.Tests.Fakes;
using Xunit;

namespace ARC.Api.Tests;

/// <summary>
/// Tests that MCP errors are sanitized and do not leak sensitive data.
/// </summary>
public sealed class McpErrorSafetyTests : IClassFixture<ArcApiTestFactory>
{
    private readonly ArcApiTestFactory _factory;

    public McpErrorSafetyTests(ArcApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MCP_Authorization_Failure_Does_Not_Leak_Internal_Details()
    {
        // Arrange
        var client = _factory.CreateClient();
        
        // Request without proper authorization
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { method = "tools/call", @params = new { name = "decideNotice", arguments = new { dealerUrn = "DEALER001" } } }),
                Encoding.UTF8,
                "application/json")
        };

        // Act
        var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert - Error should not contain stack traces, connection strings, or SQL details
        Assert.DoesNotContain("ConnectionString", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at System.", responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain("at Microsoft.", responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain("CosmosClient", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlConnection", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MCP_Unexpected_Exception_Does_Not_Expose_Secrets()
    {
        // Arrange
        var client = _factory.CreateClient();
        
        // Malformed request that might trigger unexpected exception
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                "invalid json {{{",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add(ArcActorHttp.UpnHeader, "test@example.com");
        request.Headers.Add(ArcActorHttp.RoleHeader, "DepotManager");
        request.Headers.Add(ArcActorHttp.RegionHeader, "North");
        request.Headers.Add(ArcActorHttp.DepotHeader, "DEL");

        // Act
        var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert - Should not contain tokens, keys, or connection strings
        Assert.DoesNotContain("password", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccountKey", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccountEndpoint", responseBody, StringComparison.OrdinalIgnoreCase);
    }
}

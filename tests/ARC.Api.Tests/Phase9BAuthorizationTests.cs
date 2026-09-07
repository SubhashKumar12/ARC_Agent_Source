using System.Net;
using System.Text;
using System.Text.Json;
using ARC.Api.Auth;
using ARC.Api.Tests.Fakes;
using Xunit;

namespace ARC.Api.Tests;

public sealed class Phase9BAuthorizationTests : IClassFixture<ArcApiTestFactory>
{
    private readonly ArcApiTestFactory _factory;

    public Phase9BAuthorizationTests(ArcApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Mcp_same_region_tsi_is_allowed()
    {
        var response = await CallComputeNetExposure("Tsi", "North", "DEL", "DEALER-NORTH");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("outside the actor", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mcp_cross_region_tsi_is_rejected()
    {
        var response = await CallComputeNetExposure("Tsi", "North", "DEL", "DEALER-WEST");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden
            || body.Contains("outside the actor", StringComparison.OrdinalIgnoreCase)
            || body.Contains("UnauthorizedAccess", StringComparison.OrdinalIgnoreCase),
            $"Expected governed denial, got {response.StatusCode}: {body}");
    }

    [Fact]
    public async Task Mcp_unauthorized_role_still_rejected_for_legal_tool()
    {
        var client = _factory.CreateClient();
        var request = JsonRpc("checkSection138Eligibility", new { dealerUrn = "DEALER-NORTH", asOf = "2026-03-01" });
        request.Headers.Add(ArcActorHttp.UpnHeader, "tsi@test");
        request.Headers.Add(ArcActorHttp.RoleHeader, "Tsi");
        request.Headers.Add(ArcActorHttp.RegionHeader, "North");
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            || body.Contains("role", StringComparison.OrdinalIgnoreCase),
            body);
    }

    [Fact]
    public async Task Insights_allowed_dealer_works()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/insights/exceptions?cycleId=C1&dealerUrn=DEALER-NORTH");
        request.Headers.Add(ArcActorHttp.UpnHeader, "tsi@test");
        request.Headers.Add(ArcActorHttp.RoleHeader, "Tsi");
        request.Headers.Add(ArcActorHttp.RegionHeader, "North");
        var response = await client.SendAsync(request);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Insights_cross_region_dealer_is_rejected()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/insights/exceptions?cycleId=C1&dealerUrn=DEALER-WEST");
        request.Headers.Add(ArcActorHttp.UpnHeader, "tsi@test");
        request.Headers.Add(ArcActorHttp.RoleHeader, "Tsi");
        request.Headers.Add(ArcActorHttp.RegionHeader, "North");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpResponseMessage> CallComputeNetExposure(string role, string region, string depot, string dealerUrn)
    {
        var client = _factory.CreateClient();
        var request = JsonRpc("computeNetExposure", new { dealerUrn, asOf = "2026-03-01" });
        request.Headers.Add(ArcActorHttp.UpnHeader, "actor@test");
        request.Headers.Add(ArcActorHttp.RoleHeader, role);
        request.Headers.Add(ArcActorHttp.RegionHeader, region);
        request.Headers.Add(ArcActorHttp.DepotHeader, depot);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage JsonRpc(string tool, object arguments)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "tools/call",
                    @params = new { name = tool, arguments }
                }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        return request;
    }
}

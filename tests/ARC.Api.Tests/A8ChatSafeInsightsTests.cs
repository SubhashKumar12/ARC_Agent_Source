using System.Net;
using System.Text.Json;
using ARC.Api.Auth;
using ARC.Api.Tests.Fakes;
using Xunit;

namespace ARC.Api.Tests;

public sealed class A8ChatSafeInsightsTests : IClassFixture<ArcApiTestFactory>
{
    private readonly ArcApiTestFactory _factory;

    public A8ChatSafeInsightsTests(ArcApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Exceptions_payload_is_chat_safe_and_server_generated()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/insights/exceptions?cycleId=C1&dealerUrn=DEALER-NORTH");
        AddActor(request, "Tsi", "North", "DEL");
        var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("C1", root.GetProperty("cycleId").GetString());
        Assert.True(root.GetProperty("productionSupervisionBlockedOnApprovedStoredProcedure").GetBoolean());
        var tbc = root.GetProperty("tbcIndicators");
        Assert.Equal(9, tbc.GetArrayLength());
        Assert.Contains(tbc.EnumerateArray(), e => e.GetProperty("id").GetString() == "BrokenPtpDefinition"
            && e.GetProperty("status").GetString() == "Tbc");
        Assert.Contains(tbc.EnumerateArray(), e => e.GetProperty("id").GetString() == "LeverEffectivenessFormula");
        Assert.Contains(
            root.GetProperty("productionStoredProcedureBlockers").EnumerateArray().Select(e => e.GetString()),
            n => n == "DealerRepository.ListByRegionAsync");
        Assert.DoesNotContain("effectivenessPercent", json, StringComparison.OrdinalIgnoreCase);
        var exception = root.GetProperty("exceptions").EnumerateArray()
            .Single(e => e.GetProperty("exceptionKind").GetString() == "WaitingForHuman");
        Assert.Equal("DEALER-NORTH", exception.GetProperty("dealerUrn").GetString());
        Assert.Equal("WaitingForHuman", exception.GetProperty("workflowStatus").GetString());
        Assert.Equal("DepotManager", exception.GetProperty("waitingGate").GetString());
        var gate = exception.GetProperty("gates").EnumerateArray().Single();
        Assert.Equal("DepotManager", gate.GetProperty("gate").GetString());
        Assert.Equal("Approved", gate.GetProperty("decision").GetString());
        Assert.Equal("ok", gate.GetProperty("reason").GetString());
        Assert.False(gate.TryGetProperty("sla", out _));
        Assert.False(gate.TryGetProperty("approver", out _));
        Assert.False(gate.TryGetProperty("expiryPolicy", out _));
        Assert.False(gate.TryGetProperty("escalationRules", out _));
        Assert.DoesNotContain("PromisesToPay", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Depot_manager_supervision_is_depot_scoped()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/insights/exceptions?cycleId=C1&region=North");
        AddActor(request, "DepotManager", "North", "DEL");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var dealers = doc.RootElement.GetProperty("dealers");
        var urns = dealers.EnumerateArray().Select(d => d.GetProperty("dealerUrn").GetString()).ToArray();
        Assert.Contains("DEALER-NORTH", urns);
        Assert.DoesNotContain("DEALER-NORTH-AMD", urns);
        Assert.Equal("DEL", doc.RootElement.GetProperty("depot").GetString());
    }

    [Fact]
    public async Task Cross_depot_access_fails_closed()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/insights/exceptions?cycleId=C1&dealerUrn=DEALER-NORTH-AMD");
        AddActor(request, "DepotManager", "North", "DEL");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tsi_region_restriction_remains()
    {
        var client = _factory.CreateClient();
        var allowed = new HttpRequestMessage(HttpMethod.Get, "/v1/insights/exceptions?cycleId=C1&dealerUrn=DEALER-NORTH");
        AddActor(allowed, "Tsi", "North", "DEL");
        var allowedResponse = await client.SendAsync(allowed);
        Assert.NotEqual(HttpStatusCode.Forbidden, allowedResponse.StatusCode);

        var denied = new HttpRequestMessage(HttpMethod.Get, "/v1/insights/exceptions?cycleId=C1&dealerUrn=DEALER-WEST");
        AddActor(denied, "Tsi", "North", "DEL");
        var deniedResponse = await client.SendAsync(denied);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
    }

    [Fact]
    public void Insights_query_has_no_tbc_override_parameters()
    {
        var method = typeof(ARC.Api.Controllers.InsightsController).GetMethod("Exceptions");
        Assert.NotNull(method);
        var names = method!.GetParameters().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("tbcIndicators", names);
        Assert.DoesNotContain("dataCompleteness", names);
        Assert.DoesNotContain("provenanceStatus", names);
        Assert.DoesNotContain("exceptionKind", names);
    }

    private static void AddActor(HttpRequestMessage request, string role, string region, string depot)
    {
        request.Headers.Add(ArcActorHttp.UpnHeader, "actor@test");
        request.Headers.Add(ArcActorHttp.RoleHeader, role);
        request.Headers.Add(ArcActorHttp.RegionHeader, region);
        request.Headers.Add(ArcActorHttp.DepotHeader, depot);
    }
}

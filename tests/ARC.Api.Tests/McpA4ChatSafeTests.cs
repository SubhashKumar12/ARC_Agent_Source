using System.Net;
using System.Text;
using System.Text.Json;
using ARC.Api.Auth;
using ARC.Api.Tests.Fakes;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;

namespace ARC.Api.Tests;

public sealed class McpA4ChatSafeTests : IClassFixture<ArcApiTestFactory>
{
    private readonly ArcApiTestFactory _factory;

    public McpA4ChatSafeTests(ArcApiTestFactory factory) => _factory = factory;

    [Fact]
    public void Mcp_eligibility_does_not_accept_caller_or_llm_exposure()
    {
        var method = typeof(ARC.Api.Mcp.ArcMcpTools).GetMethod("CheckSection138EligibilityAsync");
        Assert.NotNull(method);
        var names = method!.GetParameters().Select(p => p.Name ?? "").ToArray();
        Assert.Equal(["dealerUrn", "asOf", "cancellationToken"], names);
        Assert.DoesNotContain("netRecoverableExposure", names);
        Assert.DoesNotContain("netExposure", names);
        Assert.DoesNotContain("exposure", names);
        Assert.DoesNotContain("eligible", names);
        Assert.DoesNotContain("tbcIndicators", names);
        Assert.DoesNotContain("blockReason", names);
    }

    [Fact]
    public async Task Mcp_fails_closed_when_authoritative_exposure_unavailable()
    {
        var client = _factory.CreateClient();
        var request = JsonRpc("checkSection138Eligibility", new { dealerUrn = "DEALER-NORTH", asOf = "2026-03-01" });
        AddLegal(request);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("EligibilityVerdict", text, StringComparison.Ordinal);
        Assert.Contains("blockReason", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"reason\"", text, StringComparison.Ordinal);
        Assert.Contains("not available", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tbcIndicators", text, StringComparison.Ordinal);
        Assert.Contains("LedgerRepository.ListByDealerAsync", text, StringComparison.Ordinal);
        Assert.Contains("productionLegalBlockedOnApprovedStoredProcedure", text, StringComparison.Ordinal);
        Assert.DoesNotContain("od_os_amt_updt", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void G3_approver_remains_legal()
    {
        Assert.Equal(ActorRole.Legal, GateAccess.ExpectedApprover(GateId.LegalProgression));
        Assert.False(GateAccess.CanDecide(
            new ArcActor("agent@system", ActorRole.Agent, "North", "DEL"),
            GateId.LegalProgression,
            GateDecisionStatus.Approved));
    }

    [Fact]
    public void Http_case_detail_uses_recovery_state_copy_not_clock_service()
    {
        var src = File.ReadAllText(CasesControllerPath());
        Assert.Contains("A4ChatSafeAssembler.FromRecoveryState", src, StringComparison.Ordinal);
        Assert.DoesNotContain("LimitationClockService", src, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckSection138Eligibility", src, StringComparison.Ordinal);
        Assert.DoesNotContain("DecideSection138", src, StringComparison.Ordinal);
        Assert.Equal(ActorRole.Legal, GateAccess.ExpectedApprover(GateId.LegalProgression));
    }

    private static string CasesControllerPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ARC.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "ARC.Api", "Controllers", "CasesController.cs");
    }

    private static void AddLegal(HttpRequestMessage request)
    {
        request.Headers.Add(ArcActorHttp.UpnHeader, "legal@test");
        request.Headers.Add(ArcActorHttp.RoleHeader, "Legal");
        request.Headers.Add(ArcActorHttp.RegionHeader, "North");
        request.Headers.Add(ArcActorHttp.DepotHeader, "DEL");
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

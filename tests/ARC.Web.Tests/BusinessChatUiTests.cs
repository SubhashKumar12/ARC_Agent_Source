using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ARC.Web.Services;
using ARC.Web.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace ARC.Web.Tests;

public sealed class BusinessChatUiTests : IClassFixture<ArcWebTestFactory>
{
    private readonly ArcWebTestFactory _factory;

    public BusinessChatUiTests(ArcWebTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Chat_page_renders_enterprise_layout()
    {
        var client = _factory.CreateClient();
        var content = await client.GetStringAsync("/");

        Assert.Contains("ARC Recovery Assistant", content);
        Assert.Contains("Receivables", content);
        Assert.Contains("SHADOW", content);
        Assert.Contains("arc-sidebar", content);
        Assert.Contains("arc-rail", content);
        Assert.Contains("arc-composer", content);
        Assert.Contains("arc-chip", content);
        Assert.Contains("arc-context-panel", content);
        Assert.Contains("arc-welcome", content);
        Assert.Contains("id=\"messageInput\"", content);
        Assert.DoesNotContain("scenario-grid", content);
        Assert.DoesNotContain("Shadow Actions", content);
        Assert.DoesNotContain("correlationId", content);
        Assert.DoesNotContain("getDealerDetails", content);
        Assert.DoesNotContain("groundedOnDeterministicFacts", content);
    }

    [Fact]
    public async Task Dev_demo_page_contains_scenarios()
    {
        var client = _factory.CreateClient();
        var content = await client.GetStringAsync("/DevDemo");

        for (var i = 1; i <= 9; i++)
        {
            Assert.Contains($"S{i}", content);
        }
    }

    [Fact]
    public async Task Business_chat_proxy_returns_answer_and_conversation_id()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/Index?handler=BusinessChat",
            new BusinessChatProxyRequest("Show dealer 36949 in depot 005", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("dealer_mobile", body, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("conversationId").GetString()));
        Assert.Contains("36949", root.GetProperty("answer").GetString(), StringComparison.Ordinal);
        Assert.Equal("Shadow", root.GetProperty("runMode").GetString());
    }

    [Fact]
    public async Task Business_chat_dealer_only_requests_depot_clarification()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/Index?handler=BusinessChat",
            new BusinessChatProxyRequest("Show dealer 36949", null));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Please provide the depot code", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Business_chat_follow_up_preserves_conversation_id()
    {
        var client = _factory.CreateClient();
        var first = await client.PostAsJsonAsync(
            "/Index?handler=BusinessChat",
            new BusinessChatProxyRequest("Show dealer 36949 in depot 005", null));
        var firstBody = await first.Content.ReadAsStringAsync();
        var conversationId = JsonDocument.Parse(firstBody).RootElement.GetProperty("conversationId").GetString();

        var second = await client.PostAsJsonAsync(
            "/Index?handler=BusinessChat",
            new BusinessChatProxyRequest("Which depot is this dealer under?", conversationId));
        var secondBody = await second.Content.ReadAsStringAsync();

        Assert.Contains("005", secondBody, StringComparison.Ordinal);
        using var scope = _factory.Services.CreateScope();
        var fake = scope.ServiceProvider.GetRequiredService<IBusinessChatApiClient>() as FakeBusinessChatApiClient;
        Assert.NotNull(fake);
        var followUp = fake.Requests.Last();
        Assert.Equal(conversationId, followUp.ConversationId);
    }

    [Fact]
    public async Task Business_chat_proxy_error_returns_safe_message()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var fake = (FakeBusinessChatApiClient)scope.ServiceProvider.GetRequiredService<IBusinessChatApiClient>();
        fake.ThrowOnNextRequest = true;
        try
        {
            var response = await client.PostAsJsonAsync(
                "/Index?handler=BusinessChat",
                new BusinessChatProxyRequest("Show dealer 36949 in depot 005", null));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("temporarily unavailable", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        }
        finally
        {
            fake.ThrowOnNextRequest = false;
        }
    }

    [Fact]
    public void Business_chat_api_client_is_registered()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.IsType<FakeBusinessChatApiClient>(scope.ServiceProvider.GetRequiredService<IBusinessChatApiClient>());
    }
}

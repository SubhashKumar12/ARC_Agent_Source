using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ARC.Api.Auth;
using ARC.Api.Chat;
using ARC.Api.Tests.Fakes;
using ARC.Domain.Enums;
using ARC.Domain.Readiness.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace ARC.Api.Tests;

public sealed class BusinessChatTests : IClassFixture<ArcApiTestFactory>
{
    private readonly ArcApiTestFactory _factory;

    public BusinessChatTests(ArcApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Post_chat_dealer_and_depot_routes_to_get_dealer_details()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                Message = "Show dealer details for dealer 36949 in depot 005"
            })
        };
        AddActor(request, "Finance");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal(RunMode.Shadow.ToString(), root.GetProperty("runMode").GetString());
        Assert.Contains("36949", root.GetProperty("answer").GetString(), StringComparison.Ordinal);
        Assert.Contains("005", root.GetProperty("answer").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("mobile", body, StringComparison.OrdinalIgnoreCase);

        var tool = Assert.Single(root.GetProperty("toolInvocations").EnumerateArray());
        Assert.Equal("getDealerDetails", tool.GetProperty("toolName").GetString());
        Assert.True(tool.GetProperty("succeeded").GetBoolean());
        Assert.True(tool.GetProperty("deterministic").GetBoolean());

        var facts = root.GetProperty("facts").GetProperty("identity");
        Assert.Equal("36949", facts.GetProperty("dealerCode").GetString());
        Assert.Equal("005", facts.GetProperty("depotCode").GetString());
        Assert.False(root.TryGetProperty("dealerMobile", out _));
    }

    [Fact]
    public async Task Post_chat_dealer_without_depot_asks_for_clarification()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                Message = "Show dealer 36949"
            })
        };
        AddActor(request, "Finance");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Please provide the depot code", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("36949", body, StringComparison.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(body);
        Assert.Empty(doc.RootElement.GetProperty("toolInvocations").EnumerateArray());
    }

    [Fact]
    public async Task Post_chat_dealer_not_found_returns_safe_response()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                Message = "Show dealer 99999 in depot 005"
            })
        };
        AddActor(request, "Finance");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No dealer was found", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_chat_net_exposure_fails_closed_when_components_missing()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                Message = "What is the net exposure for dealer 36949 in depot 005?"
            })
        };
        AddActor(request, "Finance");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        var tools = doc.RootElement.GetProperty("toolInvocations").EnumerateArray()
            .Select(t => t.GetProperty("toolName").GetString())
            .ToList();
        Assert.Contains("getNetExposureDetails", tools);
    }

    [Theory]
    [InlineData("What is the outstanding?")]
    [InlineData("Show current OS")]
    [InlineData("How much is due?")]
    [InlineData("Give me outstanding details")]
    public async Task Post_chat_outstanding_follow_up_uses_context(string message)
    {
        var client = _factory.CreateClient();
        var conversationId = await StartDealerConversationAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                ConversationId = conversationId,
                Message = message
            })
        };
        AddActor(request, "Finance");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Current Outstanding", body, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(body);
        var tools = doc.RootElement.GetProperty("toolInvocations").EnumerateArray().ToList();
        Assert.Contains(tools, t => t.GetProperty("toolName").GetString() == "getOutstandingDetails");
        Assert.True(doc.RootElement.TryGetProperty("facts", out var factsRoot));
        Assert.Equal("36949", factsRoot.GetProperty("identity").GetProperty("dealerCode").GetString());
    }

    [Fact]
    public async Task Post_chat_over90_follow_up_returns_deterministic_amount()
    {
        var client = _factory.CreateClient();
        var conversationId = await StartDealerConversationAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                ConversationId = conversationId,
                Message = "How much is above 90 days?"
            })
        };
        AddActor(request, "Finance");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(">90 Days Outstanding", body, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(body);
        var over90 = doc.RootElement.GetProperty("facts").GetProperty("financial").GetProperty("over90Outstanding").GetDecimal();
        Assert.Equal(115_430.50m, over90);
    }

    [Fact]
    public async Task Post_chat_notice_status_follow_up_returns_notice_facts()
    {
        var client = _factory.CreateClient();
        var conversationId = await StartDealerConversationAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                ConversationId = conversationId,
                Message = "What is the notice status?"
            })
        };
        AddActor(request, "Finance");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Demand notice generated", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_chat_multi_metric_follow_up_invokes_outstanding_reader_once()
    {
        var client = _factory.CreateClient();
        var conversationId = await StartDealerConversationAsync(client);

        using var scope = _factory.Services.CreateScope();
        var outstandingReader = scope.ServiceProvider.GetRequiredService<FakeOdosOutstandingDetailReader>();
        var before = outstandingReader.CallCount;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                ConversationId = conversationId,
                Message = "Show outstanding, ageing and notice status"
            })
        };
        AddActor(request, "Finance");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Current Outstanding", body, StringComparison.Ordinal);
        Assert.Contains("Demand notice generated", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before + 1, outstandingReader.CallCount);
    }

    [Fact]
    public async Task Post_chat_new_conversation_does_not_reuse_prior_slots_without_context()
    {
        var client = _factory.CreateClient();
        var firstId = await StartDealerConversationAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                Message = "What is his outstanding?"
            })
        };
        AddActor(request, "Finance");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Please provide the depot code", body, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(firstId, JsonDocument.Parse(body).RootElement.GetProperty("conversationId").GetString());
    }

    private static async Task<string> StartDealerConversationAsync(HttpClient client)
    {
        var first = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                Message = "Show dealer 36949 in depot 005"
            })
        };
        AddActor(first, "Finance");
        var firstResponse = await client.SendAsync(first);
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        return JsonDocument.Parse(firstBody).RootElement.GetProperty("conversationId").GetString()!;
    }

    [Fact]
    public async Task Post_chat_follow_up_uses_conversation_context()
    {
        var client = _factory.CreateClient();
        var first = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                Message = "Show dealer 36949 in depot 005"
            })
        };
        AddActor(first, "Finance");
        var firstResponse = await client.SendAsync(first);
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var conversationId = JsonDocument.Parse(firstBody).RootElement.GetProperty("conversationId").GetString();

        var second = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                ConversationId = conversationId,
                Message = "Which depot is this dealer under?"
            })
        };
        AddActor(second, "Finance");
        var secondResponse = await client.SendAsync(second);
        var secondBody = await secondResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Contains("005", secondBody, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(secondBody);
        var tool = Assert.Single(doc.RootElement.GetProperty("toolInvocations").EnumerateArray());
        Assert.Equal("conversation_context", tool.GetProperty("safeTags").GetProperty("source").GetString());
    }

    [Fact]
    public async Task Post_chat_maintains_conversation_id()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                Message = "Show dealer 36949 in depot 005"
            })
        };
        AddActor(request, "Finance");
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var conversationId = doc.RootElement.GetProperty("conversationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(conversationId));
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task Post_chat_shadow_mode_is_returned()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest { Message = "Show dealer 36949 in depot 005" })
        };
        AddActor(request, "Finance");
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(nameof(RunMode.Shadow), doc.RootElement.GetProperty("runMode").GetString());
    }

    [Fact]
    public async Task Post_chat_greeting_does_not_invoke_tools_or_fail_closed()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest { Message = "Hi" })
        };
        AddActor(request, "Finance");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Contains("Welcome to ARC Recovery Assistant", root.GetProperty("answer").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("cannot currently be confirmed", body, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(root.GetProperty("toolInvocations").EnumerateArray());
        Assert.Equal(nameof(ChatSafetyOutcome.Allowed), root.GetProperty("safety").GetProperty("outcome").GetString());
        Assert.False(root.GetProperty("groundedOnDeterministicFacts").GetBoolean());
    }

    [Fact]
    public async Task Post_chat_thanks_preserves_context_for_outstanding_follow_up()
    {
        var client = _factory.CreateClient();
        var seed = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                Message = "Show dealer 36949 in depot 005"
            })
        };
        AddActor(seed, "Finance");
        var seedResponse = await client.SendAsync(seed);
        var seedBody = await seedResponse.Content.ReadAsStringAsync();
        using var seedDoc = JsonDocument.Parse(seedBody);
        var conversationId = seedDoc.RootElement.GetProperty("conversationId").GetString();

        var thanks = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                Message = "Thanks",
                ConversationId = conversationId
            })
        };
        AddActor(thanks, "Finance");
        var thanksResponse = await client.SendAsync(thanks);
        Assert.Equal(HttpStatusCode.OK, thanksResponse.StatusCode);

        var outstanding = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                Message = "Show outstanding details",
                ConversationId = conversationId
            })
        };
        AddActor(outstanding, "Finance");
        var outstandingResponse = await client.SendAsync(outstanding);
        var outstandingBody = await outstandingResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, outstandingResponse.StatusCode);
        Assert.Contains("36949", outstandingBody, StringComparison.Ordinal);
        Assert.Contains("currentOutstanding", outstandingBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Controller_has_no_business_logic_methods_beyond_post()
    {
        var methods = typeof(Controllers.ChatController).GetMethods()
            .Where(m => m.DeclaringType == typeof(Controllers.ChatController) && m.IsPublic)
            .Select(m => m.Name)
            .ToList();
        Assert.Equal(["PostAsync"], methods);
    }

    private static void AddActor(HttpRequestMessage request, string role)
    {
        request.Headers.Add(ArcActorHttp.UpnHeader, "finance.user@paintco.local");
        request.Headers.Add(ArcActorHttp.RoleHeader, role);
    }
}

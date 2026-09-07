using System.Net;
using System.Net.Http.Json;
using ARC.Api.Auth;
using ARC.Api.Chat;
using ARC.Api.Tests.Fakes;

namespace ARC.Api.Tests;

public sealed class BusinessChatPartialResponseTests : IClassFixture<ArcApiNoSessionTestFactory>
{
    private readonly ArcApiNoSessionTestFactory _factory;

    public BusinessChatPartialResponseTests(ArcApiNoSessionTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Recovery_summary_without_odos_session_returns_partial_response_not_conflict()
    {
        var client = _factory.CreateClient();
        var conversationId = Guid.NewGuid().ToString("N");

        var seed = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                Message = "Show dealer 36949 in depot 005",
                ConversationId = conversationId
            })
        };
        AddActor(seed);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(seed)).StatusCode);

        var summary = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                Message = "Give me a complete recovery summary",
                ConversationId = conversationId
            })
        };
        AddActor(summary);

        var response = await client.SendAsync(summary);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Demo Dealer", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("temporarily unavailable", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("409", body);
    }

    [Fact]
    public async Task Outstanding_question_without_odos_session_returns_structured_unavailability()
    {
        var client = _factory.CreateClient();
        var conversationId = Guid.NewGuid().ToString("N");

        var seed = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                Message = "Show dealer 36949 in depot 005",
                ConversationId = conversationId
            })
        };
        AddActor(seed);
        await client.SendAsync(seed);

        var outstanding = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new BusinessChatApiRequest
            {
                Message = "What is the current outstanding?",
                ConversationId = conversationId
            })
        };
        AddActor(outstanding);

        var response = await client.SendAsync(outstanding);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("temporarily unavailable", body, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddActor(HttpRequestMessage request)
    {
        request.Headers.Add(ArcActorHttp.UpnHeader, "demo.manager@arc.local");
        request.Headers.Add(ArcActorHttp.RoleHeader, "Finance");
    }
}

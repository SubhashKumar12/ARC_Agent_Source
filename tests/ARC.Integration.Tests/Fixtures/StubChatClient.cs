using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace ARC.Integration.Tests.Fixtures;

/// <summary>Empty narration client — mirrors Host ShadowNarrationChatClient (no Azure OpenAI).</summary>
internal sealed class StubChatClient : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new("arc-integration-stub");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "integration-stub")]));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "integration-stub");
        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

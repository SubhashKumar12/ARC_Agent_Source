using System.Collections.Concurrent;

namespace ARC.Api.Chat;

public sealed class InMemoryBusinessChatConversationStore : IBusinessChatConversationStore
{
    private readonly ConcurrentDictionary<string, BusinessChatConversationContext> _sessions = new(StringComparer.Ordinal);

    public Task<BusinessChatConversationContext?> GetAsync(string conversationId, CancellationToken cancellationToken)
        => Task.FromResult(_sessions.TryGetValue(conversationId, out var context) ? context : null);

    public Task SaveAsync(BusinessChatConversationContext context, CancellationToken cancellationToken)
    {
        _sessions[context.ConversationId] = context with { UpdatedUtc = DateTimeOffset.UtcNow };
        return Task.CompletedTask;
    }
}

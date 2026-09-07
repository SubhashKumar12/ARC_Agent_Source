using ARC.Domain.BusinessChat;

namespace ARC.Api.Chat;

public sealed record BusinessChatConversationContext(
    string ConversationId,
    string? DepotCode,
    string? DealerCode,
    string? DealerName,
    string? DepotName,
    string? DepotRegion,
    string? LastCapabilityId,
    string? LastCorrelationId,
    DateTimeOffset UpdatedUtc,
    string? PeriodKey = null,
    BusinessCapability? LastCapability = null,
    IReadOnlyList<BusinessMetric>? LastRequestedMetrics = null);

public interface IBusinessChatConversationStore
{
    Task<BusinessChatConversationContext?> GetAsync(string conversationId, CancellationToken cancellationToken);
    Task SaveAsync(BusinessChatConversationContext context, CancellationToken cancellationToken);
}

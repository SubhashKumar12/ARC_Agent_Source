using Microsoft.Azure.Cosmos;
using ARC.Data.Exceptions;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Cosmos;

public sealed class ConversationStateRepository : IConversationStateRepository
{
    private readonly ICosmosClientFactory _cosmos;

    public ConversationStateRepository(ICosmosClientFactory cosmos) => _cosmos = cosmos;

    public async Task SaveAsync(CycleId cycleId, DealerUrn dealerUrn, string payloadJson, CancellationToken cancellationToken)
    {
        var document = new ConversationDocument
        {
            id = Id(cycleId, dealerUrn),
            cycleId = cycleId.Value,
            dealerUrn = dealerUrn.Value,
            payloadJson = payloadJson,
            updatedUtc = DateTimeOffset.UtcNow
        };
        try
        {
            await _cosmos.Conversation.UpsertItemAsync(document, new PartitionKey(document.cycleId), cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to save conversation state.", ex);
        }
    }

    public async Task<string?> GetAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _cosmos.Conversation.ReadItemAsync<ConversationDocument>(
                Id(cycleId, dealerUrn), new PartitionKey(cycleId.Value), cancellationToken: cancellationToken);
            return response.Resource.payloadJson;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to load conversation state.", ex);
        }
    }

    private static string Id(CycleId cycleId, DealerUrn dealerUrn) => $"{cycleId.Value}:{dealerUrn.Value}";

    private sealed class ConversationDocument
    {
        public string id { get; set; } = "";
        public string cycleId { get; set; } = "";
        public string dealerUrn { get; set; } = "";
        public string payloadJson { get; set; } = "";
        public DateTimeOffset updatedUtc { get; set; }
    }
}

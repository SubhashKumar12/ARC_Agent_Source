using Microsoft.Extensions.AI;

namespace ARC.Agents.Ai;

/// <summary>
/// Resolves a chat client for a logical capability. Hosts bind implementations via DI.
/// </summary>
public interface IChatClientProvider
{
    IChatClient GetClient(ChatCapability capability);
}

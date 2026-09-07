using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Agents.Ai;

namespace ARC.Agents.Common;

internal static class ArcAgentFactory
{
    public static AIAgent Create(
        IChatClientProvider chat,
        ChatCapability capability,
        string name,
        string description,
        string instructions,
        IList<AITool> tools,
        ILoggerFactory loggerFactory,
        IServiceProvider services,
        IOptions<ArcAiOptions> aiOptions)
    {
        var client = chat.GetClient(capability);
        var maxTokens = aiOptions.Value.MaxOutputTokens;
        return client.AsAIAgent(
            new ChatClientAgentOptions
            {
                Name = name,
                Description = description,
                ChatOptions = new ChatOptions
                {
                    Instructions = instructions,
                    Tools = tools,
                    MaxOutputTokens = maxTokens > 0 ? maxTokens : null
                }
            },
            loggerFactory,
            services);
    }
}

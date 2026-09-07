using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Agents.Chat;
using ARC.Agents.Guardrails;

namespace ARC.Agents.Ai;

/// <summary>
/// Maps <see cref="ChatCapability"/> to a keyed or default <see cref="IChatClient"/>.
/// Wraps every resolved client with ARC guardrails when <see cref="IArcGuardrailService"/> is registered.
/// </summary>
public sealed class ConfiguredChatClientProvider : IChatClientProvider
{
    private readonly IServiceProvider _services;
    private readonly ArcAiOptions _options;

    public ConfiguredChatClientProvider(IServiceProvider services, IOptions<ArcAiOptions> options)
    {
        _services = services;
        _options = options.Value;
    }

    public IChatClient GetClient(ChatCapability capability)
    {
        var inner = ResolveInner(capability);
        if (inner is ArcGuardedChatClient)
            return inner;

        var guardrails = _services.GetService<IArcGuardrailService>();
        if (guardrails is null)
            return inner;

        var logger = _services.GetRequiredService<ILogger<ArcGuardedChatClient>>();
        return new ArcGuardedChatClient(inner, guardrails, logger, capability);
    }

    private IChatClient ResolveInner(ChatCapability capability)
    {
        var keyed = _services.GetKeyedService<IChatClient>(capability)
            ?? _services.GetKeyedService<IChatClient>(capability.ToString());
        if (keyed is not null)
            return keyed;

        var deployment = DeploymentFor(capability);
        if (!string.IsNullOrWhiteSpace(deployment))
        {
            var byDeployment = _services.GetKeyedService<IChatClient>(deployment);
            if (byDeployment is not null)
                return byDeployment;
        }

        return _services.GetRequiredService<IChatClient>();
    }

    private string? DeploymentFor(ChatCapability capability) => capability switch
    {
        ChatCapability.CheapNarration => _options.CheapNarration.Deployment,
        ChatCapability.Reasoning => _options.Reasoning.Deployment,
        ChatCapability.Extraction => _options.Extraction.Deployment,
        _ => null
    };
}

using ARC.Domain.BusinessChat;

namespace ARC.Api.Chat;

/// <summary>
/// Optional bounded LLM resolver. Disabled unless <see cref="BusinessChatSemanticOptions.UseLlmResolver"/> is true.
/// Output is validated against <see cref="BusinessMetricCatalog"/> — unknown values are rejected.
/// </summary>
public sealed class LlmSemanticCapabilityResolver : ISemanticCapabilityResolver
{
    private readonly BusinessChatSemanticOptions _options;
    private readonly ILogger<LlmSemanticCapabilityResolver> _logger;

    public LlmSemanticCapabilityResolver(
        Microsoft.Extensions.Options.IOptions<BusinessChatSemanticOptions> options,
        ILogger<LlmSemanticCapabilityResolver> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public SemanticResolutionResult Resolve(string message, BusinessChatConversationContext? prior)
    {
        if (!_options.UseLlmResolver)
            return SemanticResolutionResult.Unrecognized("llm-disabled");

        _logger.LogDebug("LLM semantic resolver is not configured with a provider in Wave 1; rejecting.");
        return SemanticResolutionResult.Unrecognized("llm-not-configured");
    }

    public static bool TryValidateProposal(
        IEnumerable<string> capabilityNames,
        IEnumerable<string> metricNames,
        out IReadOnlyList<BusinessCapability> capabilities,
        out IReadOnlyList<BusinessMetric> metrics)
    {
        capabilities = [];
        metrics = [];

        if (ContainsInjection(capabilityNames) || ContainsInjection(metricNames))
            return false;

        var parsedCaps = new List<BusinessCapability>();
        foreach (var name in capabilityNames)
        {
            if (!Enum.TryParse<BusinessCapability>(name, ignoreCase: true, out var cap)
                || !BusinessMetricCatalog.IsKnownCapability(cap))
                return false;
            parsedCaps.Add(cap);
        }

        var parsedMetrics = new List<BusinessMetric>();
        foreach (var name in metricNames)
        {
            if (!Enum.TryParse<BusinessMetric>(name, ignoreCase: true, out var metric)
                || !BusinessMetricCatalog.IsKnownMetric(metric))
                return false;
            parsedMetrics.Add(metric);
        }

        capabilities = parsedCaps;
        metrics = parsedMetrics;
        return true;
    }

    private static bool ContainsInjection(IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            var text = value ?? "";
            if (text.Contains("select ", StringComparison.OrdinalIgnoreCase)
                || text.Contains("usp_", StringComparison.OrdinalIgnoreCase)
                || text.Contains("dbo.", StringComparison.OrdinalIgnoreCase)
                || text.Contains("getDealer", StringComparison.OrdinalIgnoreCase)
                || text.Contains("tool", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

public sealed class BusinessChatSemanticOptions
{
    public const string SectionName = "ArcApi:BusinessChat:Semantic";

    public bool UseLlmResolver { get; set; }
}

/// <summary>
/// Tries optional LLM first when enabled; always falls back to deterministic resolver.
/// </summary>
public sealed class CompositeSemanticCapabilityResolver : ISemanticCapabilityResolver
{
    private readonly DeterministicSemanticCapabilityResolver _deterministic;
    private readonly LlmSemanticCapabilityResolver _llm;

    public CompositeSemanticCapabilityResolver(
        DeterministicSemanticCapabilityResolver deterministic,
        LlmSemanticCapabilityResolver llm)
    {
        _deterministic = deterministic;
        _llm = llm;
    }

    public SemanticResolutionResult Resolve(string message, BusinessChatConversationContext? prior)
    {
        var llm = _llm.Resolve(message, prior);
        if (llm.Kind != SemanticResolutionKind.Unrecognized)
            return llm;

        return _deterministic.Resolve(message, prior);
    }
}

namespace ARC.Agents.Ai;

/// <summary>
/// Chat/narration cost policy. Deployments are environment-specific and must not be hard-coded in agents.
/// </summary>
public sealed class ArcAiOptions
{
    public const string SectionName = "ArcAi";

    /// <summary>Shadow (default) or AzureOpenAI. Hosts register the matching <see cref="IChatClient"/> implementations.</summary>
    public string Provider { get; set; } = "Shadow";

    /// <summary>Azure OpenAI / Foundry endpoint. Empty in Shadow. No keys here — use managed identity.</summary>
    public string Endpoint { get; set; } = "";

    public bool UseManagedIdentity { get; set; } = true;

    /// <summary>When false, agents skip explanation calls. Tool results remain authoritative.</summary>
    public bool NarrationEnabled { get; set; } = true;

    /// <summary>Completion cap for narration/explanation. Does not apply to money/legal tools.</summary>
    public int MaxOutputTokens { get; set; } = 400;

    public ChatDeploymentOptions CheapNarration { get; set; } = new();
    public ChatDeploymentOptions Reasoning { get; set; } = new();
    public ChatDeploymentOptions Extraction { get; set; } = new();
}

public sealed class ChatDeploymentOptions
{
    /// <summary>Foundry/OpenAI deployment name. Empty means use the host default <c>IChatClient</c>.</summary>
    public string Deployment { get; set; } = "";
}

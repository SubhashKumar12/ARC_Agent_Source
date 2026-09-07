namespace ARC.Agents.Guardrails;

public enum ArcGuardrailPhase
{
    Input = 0,
    Output = 1
}

public enum ArcGuardrailAction
{
    Allow = 0,
    Warn = 1,
    Redact = 2,
    Block = 3
}

public sealed record ArcGuardrailFinding(
    string Category,
    string Provider,
    string Detail);

public sealed record ArcGuardrailResult(
    ArcGuardrailAction Action,
    string SanitizedContent,
    bool IsBlocked,
    IReadOnlyList<ArcGuardrailFinding> Findings);

/// <summary>
/// ARC-owned model-bound text inspection. Implementations must not expose MCC types.
/// </summary>
public interface IArcGuardrailService
{
    Task<ArcGuardrailResult> EvaluateAsync(
        string content,
        ArcGuardrailPhase phase,
        string? correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class ArcGuardrailOptions
{
    public const string SectionName = "ArcGuardrails";

    /// <summary>Production and Azure hosts must leave this false.</summary>
    public bool FailOpen { get; set; }

    /// <summary>Local (CLI/demo) or Azure. Azure still must not use API keys.</summary>
    public string Backend { get; set; } = "Local";
}

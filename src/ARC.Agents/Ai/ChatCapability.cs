namespace ARC.Agents.Ai;

/// <summary>
/// Logical chat roles. Configuration maps each role to a provider deployment.
/// Agents request a capability, never a model or deployment name.
/// </summary>
public enum ChatCapability
{
    CheapNarration = 0,
    Reasoning = 1,
    Extraction = 2
}

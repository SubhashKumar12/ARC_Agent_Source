namespace ARC.Agents.Context;

/// <summary>Minimal run context shared by A1–A8. Not a financial fact.</summary>
public sealed record AgentContext(
    DateOnly AsOf,
    string? CycleId,
    string? CorrelationId,
    string? DealerUrn);

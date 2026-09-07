namespace ARC.Knowledge.Grounding;

/// <summary>Request for purpose-specific grounding. No Cosmos/SQL types.</summary>
public sealed record GroundingRequest(
    GroundingPurpose Purpose,
    string? CycleId,
    string? RecoveryCaseId,
    string? DealerUrn,
    string? ActorRegion,
    string? QueryText,
    string? CorrelationId,
    int TopK = 8);

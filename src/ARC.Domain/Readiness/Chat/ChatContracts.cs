namespace ARC.Domain.Readiness.Chat;

/// <summary>
/// Future business-chat request contract. Not wired to an implementation in Phase 13A-NB.
/// Amounts, dates, eligibility, and authorization must originate from deterministic tools.
/// </summary>
public sealed record ChatRequest(
    string SessionId,
    string ActorUpn,
    string? ActorRegion,
    string? DealerUrn,
    string Question,
    string? CorrelationId);

public sealed record ChatCitation(
    string DocumentId,
    string? BlobLocation,
    string? DocumentType,
    string? SourceSystem);

public enum ChatCapabilityAvailability
{
    Available = 0,
    UnavailableProductionData = 1,
    UnavailableExternalDecision = 2,
    UnavailableAzureProvider = 3,
    UnavailableAuthorization = 4,
    UnavailableInsufficientEvidence = 5
}

public sealed record ChatCapabilityStatus(
    string CapabilityId,
    ChatCapabilityAvailability Availability,
    string Reason,
    string? BlockingDecisionId = null);

public sealed record ChatToolInvocationSummary(
    string ToolName,
    bool Succeeded,
    bool Deterministic,
    string? CorrelationId,
    IReadOnlyDictionary<string, string>? SafeTags);

public enum ChatSafetyOutcome
{
    Allowed = 0,
    BlockedAuthorization = 1,
    BlockedGuardrail = 2,
    BlockedMissingFacts = 3,
    BlockedTbcRule = 4
}

public sealed record ChatSafetyStatus(
    ChatSafetyOutcome Outcome,
    string Detail,
    IReadOnlyList<string>? GuardrailFindings = null);

/// <summary>
/// Future business-chat response contract. Narration may explain tool output; facts come from tools only.
/// </summary>
public sealed record ChatResponse(
    string SessionId,
    string CorrelationId,
    ChatSafetyStatus Safety,
    IReadOnlyList<ChatCapabilityStatus> Capabilities,
    IReadOnlyList<ChatToolInvocationSummary> ToolInvocations,
    IReadOnlyList<ChatCitation> Citations,
    string? Narration,
    bool GroundedOnDeterministicFacts);

/// <summary>Policy constants for fail-closed chat behavior. Diagnostic and design-time only.</summary>
public static class ChatFailClosedPolicy
{
    public const string NoLlmMoney = "Monetary amounts must come from ComputeNetExposure or VerifyDraft tool output.";
    public const string NoLlmStatutoryDates = "Statutory dates must come from GetLimitationClock tool output.";
    public const string NoLlmEligibility = "Eligibility must come from checkSection138Eligibility or decideNotice.";
    public const string NoLlmAuthorization = "Gate decisions require human RequestPort approval; agents cannot authorize.";
    public const string NoLlmFallbackFacts = "Missing production facts return unavailable/TBC — no LLM invention.";
    public const string CitationsRequired = "Unsupported factual claims require citation to retrieved evidence.";
}

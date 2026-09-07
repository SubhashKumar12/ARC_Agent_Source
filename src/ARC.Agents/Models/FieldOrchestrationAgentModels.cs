using ARC.Agents.Context;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Tools.Field;

namespace ARC.Agents.Models;

public enum FieldAgentAction
{
    PlanVisit = 0,
    CapturePromiseToPay = 1,
    CheckBrokenPromise = 2
}

public sealed record FieldOrchestrationAgentRequest(
    FieldAgentAction Action,
    string DealerUrn,
    RecoveryTier Tier,
    DateOnly? CommitmentDate,
    decimal? Amount,
    decimal? SpeechConfidence,
    PromiseToPay? ExistingPromise,
    string? VoiceTranscript,
    AgentContext Context);

public sealed record FieldOrchestrationAgentResult(
    VisitTask? Visit,
    StructuredPromiseToPay? Promise,
    BrokenPromiseCheckResult? Broken,
    string? Explanation);

using ARC.Domain.Enums;
using ARC.Domain.Exceptions;
using ARC.Domain.Rules;
using ARC.Domain.ValueObjects;

namespace ARC.Domain.Entities;

/// <summary>
/// Immutable human-gate decision. Expiry is never treated as approval.
/// R4: the recommending agent cannot approve.
/// </summary>
public sealed class GateDecision
{
    public GateId Gate { get; }
    public string ActorUpn { get; }
    public ActorRole ActorRole { get; }
    public GateDecisionStatus Decision { get; }
    public string Reason { get; }
    public string? RecommendedAction { get; }
    public DateTimeOffset DecidedUtc { get; }
    public CorrelationId CorrelationId { get; }

    public bool WasOverride =>
        Decision == GateDecisionStatus.Declined
        && !string.IsNullOrWhiteSpace(RecommendedAction)
        && !string.Equals(RecommendedAction, "Declined", StringComparison.OrdinalIgnoreCase);

    private GateDecision(
        GateId gate,
        string actorUpn,
        ActorRole actorRole,
        GateDecisionStatus decision,
        string reason,
        string? recommendedAction,
        DateTimeOffset decidedUtc,
        CorrelationId correlationId)
    {
        Gate = gate;
        ActorUpn = actorUpn;
        ActorRole = actorRole;
        Decision = decision;
        Reason = reason;
        RecommendedAction = recommendedAction;
        DecidedUtc = decidedUtc;
        CorrelationId = correlationId;
    }

    public static GateDecision Create(
        GateId gate,
        string actorUpn,
        ActorRole actorRole,
        GateDecisionStatus decision,
        string reason,
        CorrelationId correlationId,
        string? recommendedAction = null,
        DateTimeOffset? decidedUtc = null)
    {
        if (string.IsNullOrWhiteSpace(actorUpn))
            throw new InvalidGateDecisionException("Actor UPN is required.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidGateDecisionException("Decision reason is required.");
        R4SegregationOfDuties.EnsureCanApprove(actorRole);
        if (decision == GateDecisionStatus.Expired && string.IsNullOrWhiteSpace(reason))
            throw new InvalidGateDecisionException("Expired gates must record reason 'gate_expired'.");

        return new GateDecision(
            gate,
            actorUpn.Trim(),
            actorRole,
            decision,
            reason.Trim(),
            recommendedAction,
            decidedUtc ?? DateTimeOffset.UtcNow,
            correlationId);
    }

    /// <summary>Safe expiry: Approved = false. Never interpret timeout as Yes.</summary>
    public static GateDecision Expire(GateId gate, CorrelationId correlationId, DateTimeOffset? decidedUtc = null)
        => Create(
            gate,
            "system.gate-timer",
            ActorRole.Finance,
            GateDecisionStatus.Expired,
            "gate_expired",
            correlationId,
            recommendedAction: "Issue",
            decidedUtc: decidedUtc);

    public bool AllowsProgression => Decision == GateDecisionStatus.Approved;
}

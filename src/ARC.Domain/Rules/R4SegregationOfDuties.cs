using ARC.Domain.Enums;
using ARC.Domain.Exceptions;

namespace ARC.Domain.Rules;

/// <summary>R4 — the agent that recommends can never approve. Enforced on GateDecision.Create.</summary>
public static class R4SegregationOfDuties
{
    public const string Id = "R4";

    public static bool CanApprove(ActorRole role) => role != ActorRole.Agent;

    public static void EnsureCanApprove(ActorRole role)
    {
        if (!CanApprove(role))
            throw new InvalidGateDecisionException("R4 segregation of duties: the recommending agent cannot approve.");
    }
}

using ARC.Domain.Enums;
using ARC.Domain.Rules;

namespace ARC.Api.Auth;

public static class GateAccess
{
    public static ActorRole ExpectedApprover(GateId gate) => gate switch
    {
        GateId.DepotManager => ActorRole.DepotManager,
        GateId.AdvocateSignature => ActorRole.Advocate,
        GateId.LegalProgression => ActorRole.Legal,
        GateId.LegalCaseFileReview => ActorRole.Legal,
        _ => throw new ArgumentOutOfRangeException(nameof(gate), gate, "Unknown gate.")
    };

    public static bool CanDecide(ArcActor actor, GateId gate, GateDecisionStatus decision)
    {
        if (!R4SegregationOfDuties.CanApprove(actor.Role))
            return false;
        if (decision == GateDecisionStatus.Expired)
            return actor.Role is ActorRole.Finance or ActorRole.Legal || actor.Role == ExpectedApprover(gate);
        return actor.Role == ExpectedApprover(gate);
    }

    public static bool CanReadDealer(ArcActor actor, string? dealerRegion, string? dealerDepot)
    {
        if (actor.Role == ActorRole.Tsi)
            return !string.IsNullOrWhiteSpace(actor.Region)
                && string.Equals(actor.Region, dealerRegion, StringComparison.OrdinalIgnoreCase);
        if (actor.Role == ActorRole.DepotManager)
        {
            if (string.IsNullOrWhiteSpace(actor.Depot) || string.IsNullOrWhiteSpace(dealerDepot))
                return false;
            return string.Equals(actor.Depot, dealerDepot, StringComparison.OrdinalIgnoreCase);
        }
        return actor.Role is not ActorRole.Agent;
    }

    public static bool CanStartRun(ArcActor actor)
        => actor.Role is ActorRole.Finance or ActorRole.DepotAdmin;

    public static string? ForcedRegion(ArcActor actor)
        => actor.Role == ActorRole.Tsi ? actor.Region : null;

    public static string? ForcedDepot(ArcActor actor)
        => actor.Role == ActorRole.DepotManager ? actor.Depot : null;
}

using ARC.Agents.Workflows;
using ARC.Domain.Enums;

namespace ARC.Api.Services;

public static class GateCatalog
{
    public static bool TryParse(string gateId, out GateId gate, out string portId)
    {
        portId = gateId.Trim();
        if (Enum.TryParse(portId, ignoreCase: true, out gate))
        {
            portId = PortId(gate);
            return true;
        }

        gate = portId.ToLowerInvariant() switch
        {
            ArcWorkflowNodes.GateDepotManager => GateId.DepotManager,
            ArcWorkflowNodes.GateAdvocateSignature => GateId.AdvocateSignature,
            ArcWorkflowNodes.GateLegalProgression => GateId.LegalProgression,
            ArcWorkflowNodes.GateLegalCaseFileReview => GateId.LegalCaseFileReview,
            _ => (GateId)(-1)
        };
        return Enum.IsDefined(gate);
    }

    public static string PortId(GateId gate) => gate switch
    {
        GateId.DepotManager => ArcWorkflowNodes.GateDepotManager,
        GateId.AdvocateSignature => ArcWorkflowNodes.GateAdvocateSignature,
        GateId.LegalProgression => ArcWorkflowNodes.GateLegalProgression,
        GateId.LegalCaseFileReview => ArcWorkflowNodes.GateLegalCaseFileReview,
        _ => throw new ArgumentOutOfRangeException(nameof(gate))
    };
}

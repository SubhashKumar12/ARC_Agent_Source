using ARC.Agents.Context;
using ARC.Tools.Evidence;

namespace ARC.Agents.Models;

public sealed record EvidenceCaseFileAgentRequest(
    string DealerUrn,
    IReadOnlyList<EvidenceItem> Documents,
    string? CaseReference,
    AgentContext Context);

public sealed record EvidenceCaseFileAgentResult(CaseFilePreparationResult CaseFile, string? Explanation);

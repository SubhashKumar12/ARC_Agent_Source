using ARC.Agents.Context;
using ARC.Domain.Entities;
using ARC.Domain.Metrics;
using ARC.Tools.Legal;

namespace ARC.Agents.Models;

public sealed record LegalEligibilityAgentRequest(
    string DealerUrn,
    ExposureBreakdown Exposure,
    DemandNotice? DemandNotice,
    AgentContext Context);

public sealed record LegalEligibilityAgentResult(
    LegalEligibilityResult Facts,
    string? Explanation,
    A4ChatSafeContract ChatSafe);

using ARC.Agents.Context;
using ARC.Domain.Metrics;
using ARC.Tools.Risk;

namespace ARC.Agents.Models;

public sealed record RiskPrioritisationAgentRequest(
    ExposureBreakdown Exposure,
    bool HasBouncedSecurityCheque,
    int? DaysSinceDemandNotice,
    string? TsiRemarks,
    AgentContext Context);

public sealed record RiskPrioritisationAgentResult(
    RiskAssessment Assessment,
    string? Explanation,
    A2ChatSafeContract? ChatSafe = null);

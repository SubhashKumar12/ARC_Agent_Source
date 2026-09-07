using ARC.Agents.Context;
using ARC.Domain.Entities;
using ARC.Domain.Metrics;
using ARC.Knowledge.Grounding;

namespace ARC.Agents.Models;

public sealed record NoticeDecisioningAgentRequest(
    Dealer Dealer,
    ExposureBreakdown Exposure,
    Dispute? OpenDispute,
    PromiseToPay? ActivePromiseToPay,
    string? SearchText,
    AgentContext Context);

public sealed record NoticeDecisioningAgentResult(
    NoticeVerdict Verdict,
    GroundingContext Grounding,
    string? Explanation);

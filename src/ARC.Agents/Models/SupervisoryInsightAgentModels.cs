using ARC.Agents.Context;
using ARC.Domain.Entities;
using ARC.Domain.Metrics;
using ARC.Knowledge.Grounding;
using ARC.Tools.Insights;
using ARC.Tools.Knowledge;

namespace ARC.Agents.Models;

public sealed record SupervisoryInsightAgentRequest(
    string CycleId,
    string? Region,
    string? DealerUrn,
    string? NaturalLanguageQuestion,
    IReadOnlyList<PromiseToPay>? PromisesToPay,
    AgentContext Context,
    string? Depot = null);

public sealed record SupervisoryInsightAgentResult(
    SupervisoryInsightResult Insights,
    GroundingContext? Grounding,
    string? Explanation,
    A8ChatSafeContract ChatSafe);

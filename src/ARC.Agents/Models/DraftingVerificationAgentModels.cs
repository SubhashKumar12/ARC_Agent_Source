using ARC.Agents.Context;
using ARC.Domain.Entities;
using ARC.Domain.Limitation;
using ARC.Domain.Metrics;
using ARC.Knowledge.Grounding;
using ARC.Tools.Drafting;

namespace ARC.Agents.Models;

public sealed record DraftingVerificationAgentRequest(
    DraftKind Kind,
    ExposureBreakdown Exposure,
    Dealer Dealer,
    SecurityCheque? Cheque,
    ChequeReturnMemo? Memo,
    LimitationClock? Clock,
    DraftQuotedFields? Draft,
    AgentContext Context);

public sealed record DraftingVerificationAgentResult(
    DraftingVerificationResult Verification,
    GroundingContext Grounding,
    string? Explanation);

using ARC.Agents.Context;
using ARC.Tools.Reconciliation;

namespace ARC.Agents.Models;

public sealed record ReconciliationAgentRequest(string DealerUrn, AgentContext Context);

public sealed record ReconciliationAgentResult(ComputeNetExposureResult Facts, string? Explanation);

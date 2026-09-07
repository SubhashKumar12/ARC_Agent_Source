using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Agents.Ai;
using ARC.Agents.Common;
using ARC.Agents.Models;
using ARC.Agents.Prompts;
using ARC.Domain.ValueObjects;
using ARC.Knowledge.Grounding;
using ARC.Knowledge.Retrieval;
using ARC.Tools.Knowledge;
using ARC.Tools.Notice;

namespace ARC.Agents.A3NoticeDecisioning;

/// <summary>A3 — notice recommendation. DecideNotice is authoritative. Does not approve G1.</summary>
public sealed class NoticeDecisioningAgent
{
    public const string Name = "A3-NoticeDecisioning";

    private readonly NoticeDecisionTool _notice;
    private readonly IGroundingContextProvider _grounding;
    private readonly ILogger<NoticeDecisioningAgent> _logger;
    private readonly ArcAiOptions _ai;

    public AIAgent Agent { get; }

    public NoticeDecisioningAgent(
        IChatClientProvider chat,
        IOptions<ArcAiOptions> aiOptions,
        NoticeDecisionTool notice,
        IGroundingContextProvider grounding,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        _notice = notice;
        _grounding = grounding;
        _logger = loggerFactory.CreateLogger<NoticeDecisioningAgent>();
        _ai = aiOptions.Value;
        Agent = ArcAgentFactory.Create(
            chat,
            ChatCapability.Reasoning,
            Name,
            "Recommends Issue, Hold, or Reconcile from deterministic rules. Does not approve G1.",
            AgentPrompts.A3,
            [
                AIFunctionFactory.Create(
                    _notice.Decide,
                    new AIFunctionFactoryOptions
                    {
                        Name = NoticeDecisionTool.Name,
                        Description = "Authoritative notice decision from rules R1/R5/R6. The model must not change Issue, Hold, or Reconcile."
                    })
            ],
            loggerFactory,
            services,
            aiOptions);
    }

    public Task<NoticeDecisioningAgentResult> RunAsync(NoticeDecisioningAgentRequest request, CancellationToken cancellationToken)
        => AgentRunGuard.ExecuteAsync(Name, _logger, request.Context, async () =>
        {
            using var scope = RetrievalScope.Enter(
                new RetrievalAuthorization(request.Dealer.Region, request.Dealer.Urn.Value));

            // Get grounded context: authoritative graph facts + reference knowledge (if query provided).
            var grounding = await _grounding.GetContextAsync(
                new GroundingRequest(
                    GroundingPurpose.A3NoticeDecisionSupport,
                    request.Context.CycleId,
                    RecoveryCaseId: null,
                    request.Dealer.Urn.Value,
                    request.Dealer.Region,
                    request.SearchText,
                    request.Context.CorrelationId),
                cancellationToken);

            // Build citations from grounding evidence.
            var citations = grounding.Citations
                .Select(c => new Citation(c.DocumentId, c.Version ?? "latest"))
                .ToList();

            // Deterministic decision (authoritative).
            var verdict = _notice.Decide(new NoticeDecisionRequest(
                request.Dealer,
                request.Exposure,
                request.Context.AsOf,
                request.OpenDispute,
                request.ActivePromiseToPay,
                citations,
                request.Context.CorrelationId));

            // LLM explanation only (does not change the decision).
            var explanation = await AgentNarration.ExplainAsync(
                Agent,
                Name,
                new
                {
                    dealerUrn = request.Dealer.Urn.Value,
                    decision = verdict.Decision.ToString(),
                    verdict.RequiresDepotManagerGate,
                    netRecoverableExposure = request.Exposure.NetRecoverableExposure.Amount,
                    rules = verdict.RuleResults.Select(r => new { r.RuleId, r.Passed, r.Message }),
                    authoritativeFacts = grounding.StructuredFacts.Select(f => new { f.Kind, f.Label, f.Summary }),
                    referenceKnowledge = grounding.KnowledgeChunks.Select(k => new { k.Title, k.Reference.DocumentId }),
                    citations = verdict.Citations.Select(c => new { c.SourceId, c.Description }),
                    insufficientEvidence = grounding.Diagnostics.InsufficientEvidence,
                    humanGate = verdict.RequiresDepotManagerGate ? "G1 Depot Manager — agent cannot approve" : "none"
                },
                _logger,
                cancellationToken,
                _ai);

            return new NoticeDecisioningAgentResult(verdict, grounding, explanation);
        });
}

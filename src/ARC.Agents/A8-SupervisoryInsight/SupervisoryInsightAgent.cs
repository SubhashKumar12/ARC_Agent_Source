using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Agents.Ai;
using ARC.Agents.Common;
using ARC.Agents.Models;
using ARC.Agents.Prompts;
using ARC.Domain.Metrics;
using ARC.Knowledge.Grounding;
using ARC.Knowledge.Retrieval;
using ARC.Tools.Insights;

namespace ARC.Agents.A8SupervisoryInsight;

/// <summary>A8 — exception queue and optional NLQ over tool facts. Not a BI platform.</summary>
public sealed class SupervisoryInsightAgent
{
    public const string Name = "A8-SupervisoryInsight";

    private readonly SupervisoryInsightTool _insights;
    private readonly IGroundingContextProvider _grounding;
    private readonly ILogger<SupervisoryInsightAgent> _logger;
    private readonly ArcAiOptions _ai;

    public AIAgent Agent { get; }

    public SupervisoryInsightAgent(
        IChatClientProvider chat,
        IOptions<ArcAiOptions> aiOptions,
        SupervisoryInsightTool insights,
        IGroundingContextProvider grounding,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        _insights = insights;
        _grounding = grounding;
        _logger = loggerFactory.CreateLogger<SupervisoryInsightAgent>();
        _ai = aiOptions.Value;
        Agent = ArcAgentFactory.Create(
            chat,
            ChatCapability.CheapNarration,
            Name,
            "Returns the ARC exception queue and explains it. Does not invent analytics.",
            AgentPrompts.A8,
            [
                AIFunctionFactory.Create(
                    _insights.GetAsync,
                    new AIFunctionFactoryOptions
                    {
                        Name = SupervisoryInsightTool.Name,
                        Description = "Authoritative exception queue from cycle/dealer state. Not a BI cube."
                    })
            ],
            loggerFactory,
            services,
            aiOptions);
    }

    public Task<SupervisoryInsightAgentResult> RunAsync(SupervisoryInsightAgentRequest request, CancellationToken cancellationToken)
        => AgentRunGuard.ExecuteAsync(Name, _logger, request.Context, async () =>
        {
            using var scope = RetrievalScope.Enter(
                new RetrievalAuthorization(request.Region, request.DealerUrn));

            // Deterministic exception queue (authoritative).
            var insights = await _insights.GetAsync(
                new SupervisoryInsightRequest(
                    request.CycleId,
                    request.Context.AsOf,
                    request.Region,
                    request.DealerUrn,
                    request.Context.CorrelationId,
                    request.PromisesToPay,
                    request.Depot),
                cancellationToken);

            var chatSafe = A8ChatSafeAssembler.Assemble(ToChatSafeFacts(request, insights));

            // Get grounded context for knowledge-backed NLQ (if provided).
            GroundingContext? grounding = null;
            if (!string.IsNullOrWhiteSpace(request.NaturalLanguageQuestion))
            {
                grounding = await _grounding.GetContextAsync(
                    new GroundingRequest(
                        GroundingPurpose.A8SupervisoryInsight,
                        request.CycleId,
                        RecoveryCaseId: null,
                        request.DealerUrn,
                        request.Region,
                        request.NaturalLanguageQuestion,
                        request.Context.CorrelationId),
                    cancellationToken);
            }

            // LLM explanation only (does not change deterministic insights).
            var explanation = await AgentNarration.ExplainAsync(
                Agent,
                Name,
                new
                {
                    question = request.NaturalLanguageQuestion,
                    authoritativeExceptions = chatSafe.Exceptions.Select(e => new
                    {
                        e.ExceptionKind,
                        e.DealerUrn,
                        e.ExceptionDetail,
                        e.WorkflowStatus,
                        e.WaitingGate
                    }),
                    authoritativeDealerCount = chatSafe.Dealers.Count,
                    authoritativeWorklist = insights.Worklist.Count,
                    authoritativeTbcIndicators = chatSafe.TbcIndicators,
                    authoritativeCompleteness = chatSafe.DataCompleteness,
                    provenanceStatus = chatSafe.Provenance.Status,
                    referenceKnowledge = grounding?.KnowledgeChunks.Select(k => new { k.Title, k.Reference.DocumentId }),
                    insufficientEvidence = grounding?.Diagnostics.InsufficientEvidence
                },
                _logger,
                cancellationToken,
                _ai);

            return new SupervisoryInsightAgentResult(insights, grounding, explanation, chatSafe);
        });

    private static A8ChatSafeFacts ToChatSafeFacts(
        SupervisoryInsightAgentRequest request,
        SupervisoryInsightResult insights)
        => new(
            request.CycleId,
            request.Region,
            request.Depot,
            insights.Exceptions.Select(e => new A8ExceptionSnapshot(e.DealerUrn, e.Kind.ToString(), e.Detail)).ToList(),
            insights.Dealers.Select(d => new A8DealerSnapshot(
                d.DealerUrn,
                d.Status,
                d.WaitingGate,
                d.Region,
                d.Depot,
                d.Gates.Select(A8ChatSafeAssembler.FromGate).ToList())).ToList(),
            BrokenPtpSuppliedByCaller: request.PromisesToPay is { Count: > 0 });
}

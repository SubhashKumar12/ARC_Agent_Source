using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Agents.Ai;
using ARC.Agents.Common;
using ARC.Agents.Models;
using ARC.Agents.Prompts;
using ARC.Knowledge.Grounding;
using ARC.Knowledge.Retrieval;
using ARC.Tools.Drafting;

namespace ARC.Agents.A5DraftingVerification;

/// <summary>A5 — quote facts into a draft and verify. Does not e-sign or approve G2.</summary>
public sealed class DraftingVerificationAgent
{
    public const string Name = "A5-DraftingVerification";

    private readonly DraftingVerificationTool _tool;
    private readonly IGroundingContextProvider _grounding;
    private readonly ILogger<DraftingVerificationAgent> _logger;
    private readonly ArcAiOptions _ai;

    public AIAgent Agent { get; }

    public DraftingVerificationAgent(
        IChatClientProvider chat,
        IOptions<ArcAiOptions> aiOptions,
        DraftingVerificationTool tool,
        IGroundingContextProvider grounding,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        _tool = tool;
        _grounding = grounding;
        _logger = loggerFactory.CreateLogger<DraftingVerificationAgent>();
        _ai = aiOptions.Value;
        Agent = ArcAgentFactory.Create(
            chat,
            ChatCapability.CheapNarration,
            Name,
            "Verifies draft fields against authoritative facts. Does not approve G2.",
            AgentPrompts.A5,
            [
                AIFunctionFactory.Create(
                    _tool.Verify,
                    new AIFunctionFactoryOptions
                    {
                        Name = DraftingVerificationTool.Name,
                        Description = "Field-by-field draft verification. A mismatch blocks the draft. Not advocate approval."
                    })
            ],
            loggerFactory,
            services,
            aiOptions);
    }

    public Task<DraftingVerificationAgentResult> RunAsync(DraftingVerificationAgentRequest request, CancellationToken cancellationToken)
        => AgentRunGuard.ExecuteAsync(Name, _logger, request.Context, async () =>
        {
            using var scope = RetrievalScope.Enter(
                new RetrievalAuthorization(request.Dealer.Region, request.Dealer.Urn.Value));

            // Get grounded context: approved/current template + dealer facts.
            var grounding = await _grounding.GetContextAsync(
                new GroundingRequest(
                    GroundingPurpose.A5DraftingTemplateSupport,
                    request.Context.CycleId,
                    RecoveryCaseId: null,
                    request.Dealer.Urn.Value,
                    request.Dealer.Region,
                    request.Kind.ToString(),
                    request.Context.CorrelationId),
                cancellationToken);

            var quoted = request.Draft ?? new DraftQuotedFields(
                request.Dealer.Urn.Value,
                request.Dealer.SapCode,
                request.Exposure.NetRecoverableExposure.Amount,
                request.Cheque?.ChequeNumber,
                request.Cheque?.Micr,
                request.Memo?.ReturnReasonCode,
                request.Memo?.MemoReceivedDate,
                request.Clock?.NoticeByDate,
                request.Clock?.CureWindowEnds,
                request.Clock?.FileByDate);

            // Deterministic verification (authoritative).
            var verification = _tool.Verify(new DraftingVerificationRequest(
                quoted,
                request.Kind,
                request.Exposure,
                request.Dealer,
                request.Cheque,
                request.Memo,
                request.Clock,
                request.Context.CycleId,
                request.Context.CorrelationId));

            // LLM explanation only (does not change verification result or approve G2).
            var explanation = await AgentNarration.ExplainAsync(
                Agent,
                Name,
                new
                {
                    passed = verification.Passed,
                    verification.ReadyForAdvocateGate,
                    mismatches = verification.Checks.Where(c => !c.Matches).Select(c => new { c.Field, c.DraftValue, c.AuthoritativeValue }),
                    authoritativeFacts = grounding.StructuredFacts.Select(f => new { f.Kind, f.Label }),
                    templates = grounding.KnowledgeChunks.Select(k => new { k.Title, k.Reference.DocumentId, k.Reference.Version }),
                    insufficientTemplate = grounding.Diagnostics.InsufficientEvidence,
                    humanGate = "G2 Advocate signature — agent cannot approve"
                },
                _logger,
                cancellationToken,
                _ai);

            return new DraftingVerificationAgentResult(verification, grounding, explanation);
        });
}

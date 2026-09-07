using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Agents.Ai;
using ARC.Agents.Common;
using ARC.Agents.Models;
using ARC.Agents.Prompts;
using ARC.Tools.Reconciliation;

namespace ARC.Agents.A1Reconciliation;

/// <summary>A1 — coordinates reconciliation. ComputeNetExposure is authoritative for amounts.</summary>
public sealed class ReconciliationAgent
{
    public const string Name = "A1-Reconciliation";

    private readonly ReconciliationTool _tool;
    private readonly ILogger<ReconciliationAgent> _logger;
    private readonly ArcAiOptions _ai;

    public AIAgent Agent { get; }

    public ReconciliationAgent(
        IChatClientProvider chat,
        IOptions<ArcAiOptions> aiOptions,
        ReconciliationTool tool,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        _tool = tool;
        _logger = loggerFactory.CreateLogger<ReconciliationAgent>();
        _ai = aiOptions.Value;
        Agent = ArcAgentFactory.Create(
            chat,
            ChatCapability.CheapNarration,
            Name,
            "Coordinates dealer ledger reconciliation. Does not calculate claim amounts.",
            AgentPrompts.A1,
            [
                AIFunctionFactory.Create(
                    _tool.ComputeNetExposureAsync,
                    new AIFunctionFactoryOptions
                    {
                        Name = ReconciliationTool.Name,
                        Description = "Authoritative net recoverable exposure with lineage. Never calculate amounts in the model."
                    })
            ],
            loggerFactory,
            services,
            aiOptions);
    }

    public Task<ReconciliationAgentResult> RunAsync(ReconciliationAgentRequest request, CancellationToken cancellationToken)
        => AgentRunGuard.ExecuteAsync(Name, _logger, request.Context, async () =>
        {
            var facts = await _tool.ComputeNetExposureAsync(
                new ComputeNetExposureRequest(
                    request.DealerUrn,
                    request.Context.AsOf,
                    request.Context.CycleId,
                    request.Context.CorrelationId),
                cancellationToken);

            var explanation = await AgentNarration.ExplainAsync(
                Agent,
                Name,
                new
                {
                    dealerUrn = facts.Exposure.DealerUrn.Value,
                    asOf = facts.Exposure.AsOf,
                    grossOpenAr = facts.Exposure.GrossOpenAr.Amount,
                    unappliedCreditNotes = facts.Exposure.UnappliedCreditNotes.Amount,
                    accruedSchemeRebates = facts.Exposure.AccruedSchemeRebates.Amount,
                    goodsReturnInTransit = facts.Exposure.GoodsReturnInTransit.Amount,
                    chequesInClearing = facts.Exposure.ChequesInClearing.Amount,
                    disputedUnderReview = facts.Exposure.DisputedUnderReview.Amount,
                    netRecoverableExposure = facts.Exposure.NetRecoverableExposure.Amount,
                    status = facts.Exposure.Status.ToString(),
                    lineageRows = facts.Exposure.Lineage.Count,
                    facts.LedgerLineCount,
                    facts.DealerUnderMoratorium
                },
                _logger,
                cancellationToken,
                _ai);

            return new ReconciliationAgentResult(facts, explanation);
        });
}

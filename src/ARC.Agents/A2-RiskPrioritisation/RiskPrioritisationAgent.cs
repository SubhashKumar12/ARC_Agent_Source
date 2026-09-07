using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Agents.Ai;
using ARC.Agents.Common;
using ARC.Agents.Models;
using ARC.Agents.Prompts;
using ARC.Domain.Metrics;
using ARC.Tools.Risk;

namespace ARC.Agents.A2RiskPrioritisation;

/// <summary>A2 — ranking and recovery tier. The tool score/tier cannot be overridden by the model.</summary>
public sealed class RiskPrioritisationAgent
{
    public const string Name = "A2-RiskPrioritisation";

    private readonly RiskPrioritisationTool _tool;
    private readonly ILogger<RiskPrioritisationAgent> _logger;
    private readonly ArcAiOptions _ai;

    public AIAgent Agent { get; }

    public RiskPrioritisationAgent(
        IChatClientProvider chat,
        IOptions<ArcAiOptions> aiOptions,
        RiskPrioritisationTool tool,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        _tool = tool;
        _logger = loggerFactory.CreateLogger<RiskPrioritisationAgent>();
        _ai = aiOptions.Value;
        Agent = ArcAgentFactory.Create(
            chat,
            ChatCapability.CheapNarration,
            Name,
            "Ranks dealers and assigns recovery tier. Does not invent risk scores.",
            AgentPrompts.A2,
            [
                AIFunctionFactory.Create(
                    _tool.Prioritise,
                    new AIFunctionFactoryOptions
                    {
                        Name = RiskPrioritisationTool.Name,
                        Description = "Authoritative recovery tier and ranking score. The model must not change the returned tier or score."
                    })
            ],
            loggerFactory,
            services,
            aiOptions);
    }

    public Task<RiskPrioritisationAgentResult> RunAsync(RiskPrioritisationAgentRequest request, CancellationToken cancellationToken)
        => AgentRunGuard.ExecuteAsync(Name, _logger, request.Context, async () =>
        {
            var assessment = _tool.Prioritise(new RiskPrioritisationRequest(
                request.Exposure,
                request.HasBouncedSecurityCheque,
                request.DaysSinceDemandNotice,
                request.Context.CorrelationId));

            var chatSafe = A2ChatSafeAssembler.FromPrioritisation(
                request.Exposure,
                assessment,
                chequeBounceKnown: true,
                demandNoticePresent: request.DaysSinceDemandNotice is not null,
                tsiRemarksPresent: !string.IsNullOrWhiteSpace(request.TsiRemarks),
                visitCutoffConfigured: _tool.VisitCutoffIsConfigured);

            var explanation = await AgentNarration.ExplainAsync(
                Agent,
                Name,
                new
                {
                    dealerUrn = chatSafe.DealerUrn,
                    scoreFormula = chatSafe.ScoreFormula,
                    recoverabilityScore = chatSafe.RecoverabilityScore,
                    tier = chatSafe.Tier,
                    deterministicExplanation = chatSafe.DeterministicExplanation,
                    tbcIndicators = chatSafe.TbcIndicators,
                    dataCompleteness = chatSafe.DataCompleteness,
                    provenanceStatus = chatSafe.Provenance.Status,
                    request.HasBouncedSecurityCheque,
                    request.DaysSinceDemandNotice,
                    tsiRemarks = request.TsiRemarks,
                    instruction = "Summarise the authoritative facts only. Do not change score or tier. Do not remove TBC flags. Do not present the interim score as the assignment composite recoverability model."
                },
                _logger,
                cancellationToken,
                _ai);

            return new RiskPrioritisationAgentResult(assessment, explanation, chatSafe);
        });
}

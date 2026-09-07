using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Agents.Ai;
using ARC.Agents.Common;
using ARC.Agents.Models;
using ARC.Agents.Prompts;
using ARC.Domain.Metrics;
using ARC.Tools.Legal;

namespace ARC.Agents.A4LegalEligibility;

/// <summary>A4 — Section 138 eligibility and limitation clock. Does not approve G3.</summary>
public sealed class LegalEligibilityAgent
{
    public const string Name = "A4-LegalEligibility";

    private readonly LegalEligibilityTool _tool;
    private readonly ILogger<LegalEligibilityAgent> _logger;
    private readonly ArcAiOptions _ai;

    public AIAgent Agent { get; }

    public LegalEligibilityAgent(
        IChatClientProvider chat,
        IOptions<ArcAiOptions> aiOptions,
        LegalEligibilityTool tool,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        _tool = tool;
        _logger = loggerFactory.CreateLogger<LegalEligibilityAgent>();
        _ai = aiOptions.Value;
        Agent = ArcAgentFactory.Create(
            chat,
            ChatCapability.Reasoning,
            Name,
            "Evaluates Section 138 eligibility and statutory clock via tools. Does not approve G3.",
            AgentPrompts.A4,
            [
                AIFunctionFactory.Create(
                    _tool.CheckSection138EligibilityAsync,
                    new AIFunctionFactoryOptions
                    {
                        Name = LegalEligibilityTool.Name,
                        Description = "Authoritative Section 138 eligibility from rules R2/R5. The model must not change Eligible."
                    }),
                AIFunctionFactory.Create(
                    _tool.GetLimitationClockAsync,
                    new AIFunctionFactoryOptions
                    {
                        Name = LegalEligibilityTool.ClockToolName,
                        Description = "Authoritative statutory dates and remaining days. The model must not calculate dates."
                    })
            ],
            loggerFactory,
            services,
            aiOptions);
    }

    public Task<LegalEligibilityAgentResult> RunAsync(LegalEligibilityAgentRequest request, CancellationToken cancellationToken)
        => AgentRunGuard.ExecuteAsync(Name, _logger, request.Context, async () =>
        {
            var facts = await _tool.CheckSection138EligibilityAsync(
                new LegalEligibilityRequest(
                    request.DealerUrn,
                    request.Context.AsOf,
                    request.Exposure,
                    request.DemandNotice,
                    request.Context.CycleId,
                    request.Context.CorrelationId),
                cancellationToken);

            var chatSafe = A4ChatSafeAssembler.FromEligibility(
                request.DealerUrn,
                facts.Eligibility,
                facts.Clock,
                facts.Alerts,
                facts.SelectedCheque,
                facts.Memo,
                request.DemandNotice,
                authoritativeExposurePresent: true);

            var explanation = await AgentNarration.ExplainAsync(
                Agent,
                Name,
                new
                {
                    request.DealerUrn,
                    eligible = chatSafe.Eligible,
                    blockReason = chatSafe.BlockReason,
                    clockStatus = chatSafe.ClockStatus,
                    chatSafe.NoticeByDate,
                    chatSafe.CureEndsDate,
                    chatSafe.FileByDate,
                    chatSafe.DaysRemaining,
                    alerts = chatSafe.DueAlerts,
                    selectedCheque = chatSafe.SelectedChequeNumber,
                    authoritativeTbcIndicators = chatSafe.TbcIndicators,
                    authoritativeCompleteness = chatSafe.DataCompleteness,
                    provenanceStatus = chatSafe.Provenance.Status,
                    humanGate = "G3 Legal progression — agent cannot approve"
                },
                _logger,
                cancellationToken,
                _ai);

            return new LegalEligibilityAgentResult(facts, explanation, chatSafe);
        });
}

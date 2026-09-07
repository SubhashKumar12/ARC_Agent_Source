using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Agents.Ai;
using ARC.Agents.Chat;
using ARC.Agents.Common;
using ARC.Agents.Exceptions;
using ARC.Agents.Guardrails;
using ARC.Agents.Models;
using ARC.Agents.Prompts;
using ARC.Tools.Field;

namespace ARC.Agents.A6FieldOrchestration;

/// <summary>A6 — visit tasks and PTP structure. Never confirms PTP. TSI remains human.</summary>
public sealed class FieldOrchestrationAgent
{
    public const string Name = "A6-FieldOrchestration";

    private readonly FieldOrchestrationTool _tool;
    private readonly IPtpTranscriptParser _parser;
    private readonly IArcGuardrailService? _guardrails;
    private readonly ILogger<FieldOrchestrationAgent> _logger;
    private readonly ArcAiOptions _ai;

    public AIAgent Agent { get; }

    public FieldOrchestrationAgent(
        IChatClientProvider chat,
        IOptions<ArcAiOptions> aiOptions,
        FieldOrchestrationTool tool,
        IPtpTranscriptParser parser,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        _tool = tool;
        _parser = parser;
        _guardrails = services.GetService<IArcGuardrailService>();
        _logger = loggerFactory.CreateLogger<FieldOrchestrationAgent>();
        _ai = aiOptions.Value;
        Agent = ArcAgentFactory.Create(
            chat,
            ChatCapability.Extraction,
            Name,
            "Plans visits and structures PTP. Does not confirm PTP or approve legal gates.",
            AgentPrompts.A6,
            [
                AIFunctionFactory.Create(
                    _tool.PlanVisitAsync,
                    new AIFunctionFactoryOptions
                    {
                        Name = "PlanVisit",
                        Description = "Create a stable visit task from dealer facts. Not a geo-routing platform."
                    }),
                AIFunctionFactory.Create(
                    _tool.CapturePromiseToPay,
                    new AIFunctionFactoryOptions
                    {
                        Name = "CapturePromiseToPay",
                        Description = "Structure a PTP. Never marks ConfirmedByTsi. TSI must confirm."
                    }),
                AIFunctionFactory.Create(
                    _tool.CheckBrokenPromise,
                    new AIFunctionFactoryOptions
                    {
                        Name = "CheckBrokenPromise",
                        Description = "Deterministic broken-PTP check against as-of date."
                    })
            ],
            loggerFactory,
            services,
            aiOptions);
    }

    public Task<FieldOrchestrationAgentResult> RunAsync(FieldOrchestrationAgentRequest request, CancellationToken cancellationToken)
        => AgentRunGuard.ExecuteAsync(Name, _logger, request.Context, async () =>
        {
            VisitTask? visit = null;
            StructuredPromiseToPay? promise = null;
            BrokenPromiseCheckResult? broken = null;

            switch (request.Action)
            {
                case FieldAgentAction.PlanVisit:
                    visit = await _tool.PlanVisitAsync(
                        new PlanVisitRequest(
                            request.DealerUrn,
                            request.Tier,
                            request.Context.AsOf,
                            request.Context.CycleId,
                            request.Context.CorrelationId),
                        cancellationToken);
                    break;

                case FieldAgentAction.CapturePromiseToPay:
                    var (date, amount) = await ResolvePtpAsync(request, cancellationToken);
                    promise = _tool.CapturePromiseToPay(new CapturePromiseToPayRequest(
                        request.DealerUrn,
                        date,
                        amount,
                        ConfirmedByTsi: false,
                        request.SpeechConfidence,
                        request.Context.AsOf,
                        request.Context.CycleId,
                        request.Context.CorrelationId));
                    break;

                case FieldAgentAction.CheckBrokenPromise:
                    if (request.ExistingPromise is null)
                        throw new AgentException(Name, "ExistingPromise is required to check a broken PTP.");
                    broken = _tool.CheckBrokenPromise(new BrokenPromiseCheckRequest(
                        request.ExistingPromise,
                        request.Context.AsOf,
                        request.Context.CorrelationId));
                    break;

                default:
                    throw new AgentException(Name, $"Unsupported field action '{request.Action}'.");
            }

            var explanation = await AgentNarration.ExplainAsync(
                Agent,
                Name,
                new
                {
                    request.Action,
                    visit,
                    ptpRecordId = promise?.RecordId,
                    requiresTsi = promise?.RequiresTsiConfirmation,
                    discarded = promise?.DiscardedLowConfidence,
                    broken = broken?.IsBroken,
                    humanGate = "TSI PTP confirmation — agent cannot confirm"
                },
                _logger,
                cancellationToken,
                _ai);

            return new FieldOrchestrationAgentResult(visit, promise, broken, explanation);
        });

    private async Task<(DateOnly Date, decimal Amount)> ResolvePtpAsync(
        FieldOrchestrationAgentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CommitmentDate is { } date && request.Amount is { } amount)
            return (date, amount);

        var transcript = request.VoiceTranscript;
        if (_guardrails is not null && !string.IsNullOrWhiteSpace(transcript))
        {
            var sanitized = await _guardrails.EvaluateAsync(transcript, ArcGuardrailPhase.Input, request.Context.CorrelationId, cancellationToken);
            if (sanitized.IsBlocked)
                throw new AgentException(Name, "Voice transcript was blocked by guardrails. TSI must capture the commitment.");
            transcript = sanitized.SanitizedContent;
        }

        var parsed = _parser.Parse(transcript, request.Context.AsOf);
        if (parsed.IsComplete)
            return (parsed.CommitmentDate!.Value, parsed.Amount!.Value);

        if (string.IsNullOrWhiteSpace(transcript))
            throw new AgentException(Name, "Provide CommitmentDate and Amount, or a VoiceTranscript for candidate extraction.");

        if (_guardrails is null)
            throw new AgentException(Name, "Voice PTP extraction requires guardrails. TSI must capture the commitment.");

        try
        {
            var response = await Agent.RunAsync<VoicePtpExtract>(
                "Extract a candidate Promise-to-Pay from the transcript. Return JSON with commitmentDate (yyyy-MM-dd or null) and amount (number or null). "
                + "Do not invent missing values. This is a candidate only — TSI must confirm. Do not treat this as an authoritative ledger amount.\n\n"
                + transcript,
                session: null,
                serializerOptions: JsonSerializerOptions.Web,
                options: null,
                cancellationToken: cancellationToken);

            if (response.Result is { CommitmentDate: { } extractedDate, Amount: > 0 } extracted)
                return (extractedDate, extracted.Amount.Value);
        }
        catch (ArcGuardrailBlockedException ex)
        {
            throw new AgentException(Name, "Voice PTP extraction was blocked by guardrails. TSI must capture it.", ex);
        }

        throw new AgentException(Name, "Voice PTP extraction did not return a usable commitment date and amount. TSI must capture it.");
    }

    private sealed record VoicePtpExtract(DateOnly? CommitmentDate, decimal? Amount);
}

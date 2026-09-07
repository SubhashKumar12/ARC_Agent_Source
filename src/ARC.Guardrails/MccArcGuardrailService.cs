using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MCC.Foundation.Guardrails;
using MCC.Foundation.Guardrails.Abstractions;
using MCC.Foundation.Guardrails.Utilities;
using ARC.Agents.Guardrails;

namespace ARC.Guardrails;

/// <summary>Thin MCC adapter. Callers see only ARC types.</summary>
public sealed class MccArcGuardrailService : IArcGuardrailService
{
    private readonly IGuardrailPipeline _pipeline;
    private readonly ArcGuardrailOptions _options;
    private readonly ILogger<MccArcGuardrailService> _logger;

    public MccArcGuardrailService(
        IGuardrailPipeline pipeline,
        IOptions<ArcGuardrailOptions> options,
        ILogger<MccArcGuardrailService> logger)
    {
        _pipeline = pipeline;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ArcGuardrailResult> EvaluateAsync(
        string content,
        ArcGuardrailPhase phase,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var text = content ?? string.Empty;
        try
        {
            var context = new GuardrailContext
            {
                Content = text,
                Phase = phase == ArcGuardrailPhase.Output ? GuardrailPhase.Output : GuardrailPhase.Input
            };
            if (!string.IsNullOrWhiteSpace(correlationId) && context.Metadata is not null)
                context.Metadata["correlation_id"] = correlationId;

            var pipeline = await _pipeline.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);
            var sanitized = ArcChequeMicrTokenizer.Tokenize(pipeline.Content ?? text);
            var action = MapAction(pipeline.FinalAction);
            var findings = pipeline.Results.Select(MapFinding).ToList();

            _logger.LogInformation(
                "Guardrail {Action} phase {Phase} correlation {CorrelationId} blocked {Blocked} findings {FindingCount}",
                action,
                phase,
                correlationId,
                pipeline.IsBlocked,
                findings.Count);

            return new ArcGuardrailResult(action, sanitized, pipeline.IsBlocked, findings);
        }
        catch (GuardrailViolationException ex)
        {
            _logger.LogInformation(
                "Guardrail blocked phase {Phase} correlation {CorrelationId} provider {Provider}",
                phase,
                correlationId,
                ex.Provider);
            return new ArcGuardrailResult(
                ArcGuardrailAction.Block,
                string.Empty,
                true,
                [new ArcGuardrailFinding("Block", ex.Provider, "blocked")]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Guardrail evaluation failed phase {Phase} correlation {CorrelationId}", phase, correlationId);
            if (_options.FailOpen)
                return new ArcGuardrailResult(ArcGuardrailAction.Allow, ArcChequeMicrTokenizer.Tokenize(text), false, []);

            return new ArcGuardrailResult(
                ArcGuardrailAction.Block,
                string.Empty,
                true,
                [new ArcGuardrailFinding("Unavailable", "ARC", "guardrail_unavailable")]);
        }
    }

    private static ArcGuardrailAction MapAction(GuardrailAction action) => action switch
    {
        GuardrailAction.Warn => ArcGuardrailAction.Warn,
        GuardrailAction.Redact => ArcGuardrailAction.Redact,
        GuardrailAction.Block => ArcGuardrailAction.Block,
        _ => ArcGuardrailAction.Allow
    };

    private static ArcGuardrailFinding MapFinding(GuardrailResult result)
        => new(
            result.Action.ToString(),
            result.Provider,
            string.IsNullOrWhiteSpace(result.Detail) ? result.Action.ToString() : "finding");
}

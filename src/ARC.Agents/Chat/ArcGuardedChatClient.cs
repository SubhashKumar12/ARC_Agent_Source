using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ARC.Agents.Ai;
using ARC.Agents.Guardrails;
using ARC.Agents.Observability;

namespace ARC.Agents.Chat;

/// <summary>
/// Central model-call interceptor: guardrails, token accounting, OTel. Never logs prompt or completion text.
/// </summary>
public sealed class ArcGuardedChatClient : IChatClient
{
    private readonly IChatClient _inner;
    private readonly IArcGuardrailService _guardrails;
    private readonly ILogger<ArcGuardedChatClient> _logger;
    private readonly ChatCapability _capability;

    public ArcGuardedChatClient(
        IChatClient inner,
        IArcGuardrailService guardrails,
        ILogger<ArcGuardedChatClient> logger,
        ChatCapability capability)
    {
        _inner = inner;
        _guardrails = guardrails;
        _logger = logger;
        _capability = capability;
    }

    public IChatClient InnerClient => _inner;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var call = ArcCallContext.Current;
        var spanName = _capability == ChatCapability.Extraction ? ArcTelemetry.LlmExtract : ArcTelemetry.LlmExplain;
        using var activity = ArcTelemetry.StartLlm(spanName);
        TagCall(activity, call);

        var inspected = await InspectMessagesAsync(messages, ArcGuardrailPhase.Input, call?.CorrelationId, cancellationToken)
            .ConfigureAwait(false);
        ArcTelemetry.Set(activity, "guardrail_action", inspected.Action.ToString());

        if (inspected.Blocked)
        {
            _logger.LogInformation(
                "LLM input blocked capability {Capability} correlation {CorrelationId} category {Category}",
                _capability,
                call?.CorrelationId,
                inspected.Category);
            if (_capability == ChatCapability.Extraction)
                throw new ArcGuardrailBlockedException("Model input was blocked by guardrails.");
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty));
        }

        ChatResponse response;
        try
        {
            response = await _inner.GetResponseAsync(inspected.Messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not ArcGuardrailBlockedException)
        {
            _logger.LogWarning(
                ex,
                "LLM call failed capability {Capability} correlation {CorrelationId}",
                _capability,
                call?.CorrelationId);
            throw;
        }

        RecordUsage(activity, response);

        var outputText = response.Text;
        if (!string.IsNullOrEmpty(outputText))
        {
            var output = await _guardrails.EvaluateAsync(outputText, ArcGuardrailPhase.Output, call?.CorrelationId, cancellationToken)
                .ConfigureAwait(false);
            ArcTelemetry.Set(activity, "guardrail_output_action", output.Action.ToString());
            if (output.IsBlocked)
            {
                _logger.LogInformation(
                    "LLM output blocked capability {Capability} correlation {CorrelationId}",
                    _capability,
                    call?.CorrelationId);
                if (_capability == ChatCapability.Extraction)
                    throw new ArcGuardrailBlockedException("Model output was blocked by guardrails.");
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty));
            }
        }

        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var inspected = await InspectMessagesAsync(messages, ArcGuardrailPhase.Input, ArcCallContext.Current?.CorrelationId, cancellationToken)
            .ConfigureAwait(false);
        if (inspected.Blocked)
            yield break;

        await foreach (var update in _inner.GetStreamingResponseAsync(inspected.Messages, options, cancellationToken))
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType.IsInstanceOfType(this))
            return this;
        return _inner.GetService(serviceType, serviceKey);
    }

    public void Dispose() => _inner.Dispose();

    private async Task<InspectedMessages> InspectMessagesAsync(
        IEnumerable<ChatMessage> messages,
        ArcGuardrailPhase phase,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var list = messages.ToList();
        var sanitized = new List<ChatMessage>(list.Count);
        var action = ArcGuardrailAction.Allow;
        string? category = null;

        foreach (var message in list)
        {
            var text = Concatenate(message);
            if (string.IsNullOrEmpty(text) || message.Role == ChatRole.System)
            {
                sanitized.Add(message);
                continue;
            }

            var result = await _guardrails.EvaluateAsync(text, phase, correlationId, cancellationToken).ConfigureAwait(false);
            if (result.Action > action)
                action = result.Action;
            if (result.IsBlocked)
            {
                category = result.Findings.FirstOrDefault()?.Category;
                return new InspectedMessages(sanitized, true, result.Action, category);
            }

            sanitized.Add(ReplaceText(message, result.SanitizedContent));
        }

        return new InspectedMessages(sanitized, false, action, category);
    }

    private void RecordUsage(System.Diagnostics.Activity? activity, ChatResponse response)
    {
        var usage = response.Usage;
        var input = usage?.InputTokenCount;
        var output = usage?.OutputTokenCount;
        var total = usage?.TotalTokenCount;
        var model = response.ModelId;

        ArcTelemetry.Set(activity, "capability", _capability.ToString());
        ArcTelemetry.Set(activity, "model", model);
        if (input is > 0)
            activity?.SetTag("input_tokens", input.Value);
        if (output is > 0)
            activity?.SetTag("output_tokens", output.Value);
        if (total is > 0)
            activity?.SetTag("total_tokens", total.Value);

        if (usage is null)
        {
            _logger.LogInformation(
                "LLM usage capability {Capability} correlation {CorrelationId} tokens none model {Model}",
                _capability,
                ArcCallContext.Current?.CorrelationId,
                model);
            return;
        }

        _logger.LogInformation(
            "LLM usage capability {Capability} correlation {CorrelationId} inputTokens {InputTokens} outputTokens {OutputTokens} totalTokens {TotalTokens} model {Model}",
            _capability,
            ArcCallContext.Current?.CorrelationId,
            input,
            output,
            total,
            model);
    }

    private static void TagCall(System.Diagnostics.Activity? activity, ArcCallContext? call)
    {
        if (call is null)
            return;
        ArcTelemetry.Set(activity, "correlation_id", call.CorrelationId);
        ArcTelemetry.Set(activity, "cycle_id", call.CycleId);
        ArcTelemetry.Set(activity, "dealer_urn", call.DealerUrn);
        ArcTelemetry.Set(activity, "agent", call.AgentName);
    }

    private static string Concatenate(ChatMessage message)
    {
        if (message.Text is { Length: > 0 } text)
            return text;
        var builder = new StringBuilder();
        foreach (var content in message.Contents)
        {
            if (content is TextContent textContent)
                builder.Append(textContent.Text);
        }
        return builder.ToString();
    }

    private static ChatMessage ReplaceText(ChatMessage original, string sanitized)
        => new(original.Role, sanitized)
        {
            AdditionalProperties = original.AdditionalProperties
        };

    private readonly record struct InspectedMessages(
        IReadOnlyList<ChatMessage> Messages,
        bool Blocked,
        ArcGuardrailAction Action,
        string? Category);
}

public sealed class ArcGuardrailBlockedException : Exception
{
    public ArcGuardrailBlockedException(string message) : base(message)
    {
    }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Data.Exceptions;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Tools.Exceptions;
using ARC.Tools.Models;

namespace ARC.Tools.Field;

public sealed record VisitTask(
    string TaskId,
    string DealerUrn,
    string? Depot,
    string? Region,
    string? CoveringTsi,
    RecoveryTier Tier,
    DateOnly AsOf);

public sealed record StructuredPromiseToPay(
    string RecordId,
    PromiseToPay Promise,
    bool RequiresTsiConfirmation,
    bool DiscardedLowConfidence);

public sealed record PlanVisitRequest(
    string DealerUrn,
    RecoveryTier Tier,
    DateOnly AsOf,
    string? CycleId,
    string? CorrelationId);

public sealed record CapturePromiseToPayRequest(
    string DealerUrn,
    DateOnly CommitmentDate,
    decimal Amount,
    bool ConfirmedByTsi,
    decimal? SpeechConfidence,
    DateOnly AsOf,
    string? CycleId,
    string? CorrelationId);

public sealed record BrokenPromiseCheckRequest(
    PromiseToPay Promise,
    DateOnly AsOf,
    string? CorrelationId);

public sealed record BrokenPromiseCheckResult(bool IsBroken, DateOnly CommitmentDate, DateOnly AsOf);

/// <summary>
/// Visit-task and PTP structure for A6. No geo platform, no speech/LLM.
/// TSI confirmation remains human — this tool never confirms a PTP.
/// </summary>
public sealed class FieldOrchestrationTool
{
    public const string Name = "OrchestrateField";

    private readonly IDealerRepository _dealers;
    private readonly ArcToolsOptions _options;
    private readonly ILogger<FieldOrchestrationTool> _logger;

    public FieldOrchestrationTool(
        IDealerRepository dealers,
        IOptions<ArcToolsOptions> options,
        ILogger<FieldOrchestrationTool> logger)
    {
        _dealers = dealers;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<VisitTask> PlanVisitAsync(PlanVisitRequest request, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(request.DealerUrn))
            throw new ToolException(Name, "DealerUrn is required.");
        if (string.IsNullOrWhiteSpace(request.CycleId))
            throw new ToolException(Name, "CycleId is required for a stable visit task identifier.");

        try
        {
            var urn = new DealerUrn(request.DealerUrn);
            var dealer = await _dealers.GetAsync(urn, cancellationToken)
                ?? throw new ToolException(Name, $"Dealer '{request.DealerUrn}' was not found.");

            var task = new VisitTask(
                TaskId: $"{request.CycleId}|{urn.Value}|visit",
                DealerUrn: urn.Value,
                Depot: dealer.Depot,
                Region: dealer.Region,
                CoveringTsi: dealer.CoveringTsi,
                Tier: request.Tier,
                AsOf: request.AsOf);

            _logger.LogInformation(
                "Tool {Tool} action PlanVisit dealer {DealerUrn} cycle {CycleId} correlation {CorrelationId} durationMs {DurationMs}",
                Name, request.DealerUrn, request.CycleId, request.CorrelationId,
                (DateTimeOffset.UtcNow - started).TotalMilliseconds);

            return task;
        }
        catch (DataAccessException ex)
        {
            throw new ToolException(Name, "Failed to load dealer for visit planning.", ex);
        }
    }

    public StructuredPromiseToPay CapturePromiseToPay(CapturePromiseToPayRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DealerUrn))
            throw new ToolException(Name, "DealerUrn is required.");
        if (string.IsNullOrWhiteSpace(request.CycleId))
            throw new ToolException(Name, "CycleId is required for a stable PTP identifier.");
        if (request.Amount <= 0m)
            throw new ToolException(Name, "PTP amount must be greater than zero.");

        var discarded = _options.VoicePtpDiscardBelow is { } floor
            && request.SpeechConfidence is { } confidence
            && confidence < floor;

        var requiresTsi = !request.ConfirmedByTsi
            || discarded
            || (_options.VoicePtpConfirmBelow is { } confirmBelow
                && request.SpeechConfidence is { } asr
                && asr < confirmBelow);

        var urn = new DealerUrn(request.DealerUrn);
        var ptp = new PromiseToPay(urn, request.CommitmentDate, new Money(request.Amount), confirmedByTsi: false);
        var recordId = $"{request.CycleId}|{urn.Value}|ptp|{request.CommitmentDate:yyyyMMdd}";

        _logger.LogInformation(
            "Tool {Tool} action CapturePtp dealer {DealerUrn} cycle {CycleId} correlation {CorrelationId} requiresTsi {RequiresTsi} discarded {Discarded}",
            Name, request.DealerUrn, request.CycleId, request.CorrelationId, requiresTsi, discarded);

        return new StructuredPromiseToPay(recordId, ptp, requiresTsi, discarded);
    }

    public BrokenPromiseCheckResult CheckBrokenPromise(BrokenPromiseCheckRequest request)
    {
        var broken = request.AsOf > request.Promise.CommitmentDate;
        _logger.LogInformation(
            "Tool {Tool} action BrokenPtp dealer {DealerUrn} correlation {CorrelationId} broken {Broken}",
            Name, request.Promise.DealerUrn.Value, request.CorrelationId, broken);
        return new BrokenPromiseCheckResult(broken, request.Promise.CommitmentDate, request.AsOf);
    }
}

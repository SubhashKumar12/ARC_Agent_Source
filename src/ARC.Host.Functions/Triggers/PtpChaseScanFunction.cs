using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Tools.Field;

namespace ARC.Host.Functions.Triggers;

/// <summary>
/// Periodic broken-PTP chase scanner. Schedule from configuration (%PtpChaseSchedule%).
/// Shadow may persist SuppressedShadow chases; no Live dispatch.
/// </summary>
public sealed class PtpChaseScanFunction
{
    private readonly PtpChaseScanner _scanner;
    private readonly IOptions<ArcHostOptions> _options;
    private readonly ILogger<PtpChaseScanFunction> _logger;

    public PtpChaseScanFunction(
        PtpChaseScanner scanner,
        IOptions<ArcHostOptions> options,
        ILogger<PtpChaseScanFunction> logger)
    {
        _scanner = scanner;
        _options = options;
        _logger = logger;
    }

    [Function(nameof(PtpChaseScanFunction))]
    public async Task Run(
        [TimerTrigger("%PtpChaseSchedule%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var correlation = new CorrelationId($"ptp-chase-scan|{asOf:yyyyMMdd}|{Guid.NewGuid():N}");
        var mode = _options.Value.DefaultRunMode;

        _logger.LogInformation(
            "PTP chase scan trigger asOf {AsOf} mode {Mode} correlation {CorrelationId} scheduleStatus {Status}",
            asOf, mode, correlation.Value, timer.ScheduleStatus?.Last);

        var created = await _scanner.ScanDueAsync(asOf, mode, correlation, cancellationToken);

        _logger.LogInformation(
            "PTP chase scan completed correlation {CorrelationId} newChases {NewChaseCount}",
            correlation.Value, created.Count);
    }
}

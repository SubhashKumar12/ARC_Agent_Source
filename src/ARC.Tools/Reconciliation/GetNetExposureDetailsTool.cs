using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Tools.Exceptions;
using Microsoft.Extensions.Logging;

namespace ARC.Tools.Reconciliation;

/// <summary>
/// A1 net exposure read for Business Chat. Fails closed when required components are missing.
/// </summary>
public sealed class GetNetExposureDetailsTool
{
    public const string Name = "getNetExposureDetails";

    private readonly ReconciliationTool _reconciliation;
    private readonly ILogger<GetNetExposureDetailsTool> _logger;

    public GetNetExposureDetailsTool(
        ReconciliationTool reconciliation,
        ILogger<GetNetExposureDetailsTool> logger)
    {
        _reconciliation = reconciliation;
        _logger = logger;
    }

    public async Task<NetExposureChatFacts> GetByDealerUrnAsync(
        string dealerUrn,
        DateOnly asOf,
        CancellationToken cancellationToken,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(dealerUrn))
            throw new ToolException(Name, "DealerUrn is required.");

        try
        {
            var result = await _reconciliation.ComputeNetExposureAsync(
                new ComputeNetExposureRequest(dealerUrn, asOf, null, correlationId),
                cancellationToken);

            var exposure = result.Exposure;
            var missing = new List<string>();
            if (exposure.Status != ReconciliationStatus.Reconciled)
                missing.Add("unclassified_ledger_lines");

            return new NetExposureChatFacts(
                dealerUrn,
                exposure.GrossOpenAr.Amount,
                missing.Count == 0 ? exposure.NetRecoverableExposure.Amount : null,
                exposure.Status.ToString(),
                missing);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Tool {Tool} exposure unavailable for {DealerUrn}", Name, dealerUrn);
            return new NetExposureChatFacts(
                dealerUrn,
                null,
                null,
                "NOT_AVAILABLE",
                ["authoritative_ledger", "DEC-O04", "DEC-F03"]);
        }
    }
}

public sealed record NetExposureChatFacts(
    string DealerUrn,
    decimal? GrossOpenAr,
    decimal? NetRecoverableExposure,
    string ReconciliationStatus,
    IReadOnlyList<string> MissingComponents);

using Microsoft.Extensions.Logging;
using ARC.Data.Exceptions;
using ARC.Data.Sql;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;
using ARC.Tools.Exceptions;

namespace ARC.Tools.DealerMaster;

/// <summary>
/// Deterministic dealer master read for Business Chat. No SQL, no LLM, no PII (mobile excluded).
/// </summary>
public sealed class GetDealerDetailsTool
{
    public const string Name = "GetDealerDetails";

    private readonly IDealerMasterDetailReader _dealers;
    private readonly ILogger<GetDealerDetailsTool> _logger;

    public GetDealerDetailsTool(
        IDealerMasterDetailReader dealers,
        ILogger<GetDealerDetailsTool> logger)
    {
        _dealers = dealers;
        _logger = logger;
    }

    public async Task<DealerDetailChatSafeContract?> GetByOdosKeysAsync(
        string depotCode,
        string dealerCode,
        CancellationToken cancellationToken,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(depotCode) || string.IsNullOrWhiteSpace(dealerCode))
            throw new ToolException(Name, "DepotCode and DealerCode are required.");

        try
        {
            var detail = await _dealers.GetMasterDetailByOdosKeysAsync(
                depotCode.Trim(),
                dealerCode.Trim(),
                cancellationToken);
            if (detail is null)
                return null;

            var chatSafe = DealerDetailChatSafeAssembler.FromMasterDetail(detail);
            _logger.LogInformation(
                "Tool {Tool} loaded dealer master for depot {DepotCode} dealer {DealerCode} via {DataSource}. CorrelationId={CorrelationId}",
                Name,
                detail.DepotCode,
                detail.DealerCode,
                chatSafe.DataSource,
                correlationId ?? "(none)");

            return chatSafe;
        }
        catch (DataAccessException ex)
        {
            throw new ToolException(Name, "Dealer lookup failed closed.", ex);
        }
    }

    public async Task<DealerDetailChatSafeContract?> GetAsync(
        string dealerUrn,
        CancellationToken cancellationToken,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(dealerUrn))
            throw new ToolException(Name, "DealerUrn is required.");

        try
        {
            var urn = new DealerUrn(dealerUrn.Trim());
            var detail = await _dealers.GetMasterDetailAsync(urn, cancellationToken);
            if (detail is null)
                return null;

            var chatSafe = DealerDetailChatSafeAssembler.FromMasterDetail(detail);
            _logger.LogInformation(
                "Tool {Tool} loaded dealer master for {DealerUrn} via {DataSource}. CorrelationId={CorrelationId}",
                Name,
                urn.Value,
                chatSafe.DataSource,
                correlationId ?? "(none)");

            return chatSafe;
        }
        catch (DataAccessException ex)
        {
            throw new ToolException(Name, "Dealer lookup failed closed.", ex);
        }
    }
}

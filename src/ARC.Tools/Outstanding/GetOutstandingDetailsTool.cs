using Microsoft.Extensions.Logging;
using ARC.Data.Exceptions;
using ARC.Data.Odos;
using ARC.Domain.Metrics;
using ARC.Domain.Odos;
using ARC.Tools.Exceptions;

namespace ARC.Tools.Outstanding;

/// <summary>
/// Deterministic ODOS outstanding read for Business Chat. No SQL, no LLM, no PII.
/// </summary>
public sealed class GetOutstandingDetailsTool
{
  public const string Name = "getOutstandingDetails";

  private readonly IOdosOutstandingDetailReader _reader;
  private readonly ILogger<GetOutstandingDetailsTool> _logger;

  public GetOutstandingDetailsTool(
    IOdosOutstandingDetailReader reader,
    ILogger<GetOutstandingDetailsTool> logger)
  {
    _reader = reader;
    _logger = logger;
  }

  public async Task<OutstandingDetailChatSafeContract?> GetByOdosKeysAsync(
    string depotCode,
    string dealerCode,
    CancellationToken cancellationToken,
    string? dealerName = null,
    string? depotName = null,
    string? correlationId = null)
  {
    if (string.IsNullOrWhiteSpace(depotCode) || string.IsNullOrWhiteSpace(dealerCode))
      throw new ToolException(Name, "DepotCode and DealerCode are required.");

    try
    {
      var detail = await _reader.GetByOdosKeysAsync(
        depotCode.Trim(),
        dealerCode.Trim(),
        cancellationToken);

      if (detail is null)
        return null;

      var chatSafe = OutstandingDetailChatSafeAssembler.FromOdosDetail(detail, dealerName, depotName);
      _logger.LogInformation(
        "Tool {Tool} loaded outstanding for depot {DepotCode} dealer {DealerCode} period {PeriodKey}. CorrelationId={CorrelationId}",
        Name,
        detail.DepotCode,
        detail.DealerCode,
        detail.PeriodKey,
        correlationId ?? "(none)");

      return chatSafe;
    }
    catch (OdosSessionNotConfiguredException)
    {
      throw;
    }
    catch (DataAccessException ex)
    {
      throw new ToolException(Name, "Outstanding lookup failed closed.", ex);
    }
  }
}

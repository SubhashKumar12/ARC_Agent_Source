using ARC.Data.Exceptions;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.Odos;
using Microsoft.Extensions.Options;

namespace ARC.Data.Odos;

/// <summary>
/// Read-only ODOS outstanding adapter. Composes approved SPs:
/// ODOS.usp_GetDealerDetails (header OS/ageing/notice/recovery) with
/// ODOS.usp_GetOpeningDataForArc fallback when no recovery header row is returned,
/// plus ODOS.usp_GetBusinessLineLimit when a business line is known.
/// </summary>
internal sealed class SqlOdosOutstandingDetailReader : IOdosOutstandingDetailReader
{
  private readonly IStoredProcedureExecutor _procedures;
  private readonly IOdosOpeningQuerySource _opening;
  private readonly IOdosBusinessLineLimitSource _limits;
  private readonly OdosSqlSessionOptions _session;
  private readonly SqlStoredProcedureNames _spNames;

  public SqlOdosOutstandingDetailReader(
    IStoredProcedureExecutor procedures,
    IOdosOpeningQuerySource opening,
    IOdosBusinessLineLimitSource limits,
    IOptions<OdosSqlSessionOptions> session,
    IOptions<SqlStoredProcedureNames> spNames)
  {
    _procedures = procedures;
    _opening = opening;
    _limits = limits;
    _session = session.Value;
    _spNames = spNames.Value;
  }

  public async Task<OdosOutstandingDetail?> GetByOdosKeysAsync(
    string depotCode,
    string dealerCode,
    CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(depotCode) || string.IsNullOrWhiteSpace(dealerCode))
      throw new ArgumentException("DepotCode and DealerCode are required.");

    if (!_session.IsConfigured)
      throw new OdosSessionNotConfiguredException();

    var year = ParsePeriodComponent(_session.CommtYear, nameof(_session.CommtYear));
    var month = ParsePeriodComponent(_session.CommtMonth, nameof(_session.CommtMonth));
    var depot = depotCode.Trim();
    var dealer = dealerCode.Trim();

    var sources = new List<string>();
    OdosRecoveryOutstandingRow? header = null;

    try
    {
      header = await _procedures.QuerySingleOrDefaultAsync<OdosRecoveryOutstandingRow>(
        _spNames.GetLegalCase,
        new
        {
          commt_year = _session.CommtYear,
          commt_month = _session.CommtMonth,
          depot_code = depot,
          dealer_code = dealer,
          usp_user_id = _session.UserId
        },
        cancellationToken: cancellationToken);

      if (header is not null)
        sources.Add(_spNames.GetLegalCase);
    }
    catch (Exception ex) when (ex is not DataAccessException)
    {
      throw new DataAccessException("Failed to read ODOS recovery header outstanding facts.", ex);
    }

    decimal currentOs;
    decimal os0;
    decimal os1;
    decimal os2;
    decimal os3;
    decimal os4;
    string? businessLine = header?.dlr_sbl;

    if (header is not null && HasHeaderOutstanding(header))
    {
      currentOs = header.os_amt_updt ?? 0m;
      os0 = header.os_amt0 ?? 0m;
      os1 = header.os_amt1 ?? 0m;
      os2 = header.os_amt2 ?? 0m;
      os3 = header.os_amt3 ?? 0m;
      os4 = header.os_amt4 ?? 0m;
    }
    else
    {
      var openingRows = await _opening.QueryAsync(
        new OdosOpeningQuery(year, month, depot, dealer),
        cancellationToken);

      if (openingRows.Count == 0)
        return null;

      sources.Add(_spNames.GetOdosOpeningData);
      currentOs = openingRows.Sum(r => r.OsAmtUpdt);
      os0 = openingRows.Sum(r => r.OsAmt0);
      os1 = openingRows.Sum(r => r.OsAmt1);
      os2 = openingRows.Sum(r => r.OsAmt2);
      os3 = openingRows.Sum(r => r.OsAmt3);
      os4 = openingRows.Sum(r => r.OsAmt4);
      businessLine ??= openingRows.Select(r => r.BusinessLine).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
    }

    decimal? businessLimit = header?.business_line_limit;
    if (businessLimit is null && !string.IsNullOrWhiteSpace(businessLine))
    {
      var limit = await _limits.GetLimitAsync(businessLine, cancellationToken);
      if (limit is not null)
      {
        businessLimit = limit.BusinessLimit;
        sources.Add(_spNames.GetBusinessLineLimit);
      }
    }

    return new OdosOutstandingDetail(
      depot,
      dealer,
      year,
      month,
      currentOs,
      os0,
      os1,
      os2,
      os3,
      os4,
      businessLimit,
      businessLine,
      header?.current_status_code,
      header?.current_status,
      header?.legal_status_code,
      header?.legal_status,
      header?.demand_notice_generated_yn,
      header?.notice_yn,
      header?.notice_yn_ho,
      ToDateOnly(header?.notice_date),
      ToDateOnly(header?.notice_date_ho),
      header?.notice_desc,
      header?.notice_desc_ho,
      header?.tsi_visit_status,
      header?.tsi_visit_count,
      header?.dealer_feedback,
      sources);
  }

  private static bool HasHeaderOutstanding(OdosRecoveryOutstandingRow header)
    => header.os_amt_updt is not null
       || header.os_amt0 is not null
       || header.os_amt1 is not null
       || header.os_amt2 is not null
       || header.os_amt3 is not null
       || header.os_amt4 is not null;

  private static int ParsePeriodComponent(string value, string paramName)
  {
    if (!int.TryParse(value, out var parsed))
      throw new InvalidOperationException($"ArcData:Odos:Session {paramName} must be numeric.");
    return parsed;
  }

  private static DateOnly? ToDateOnly(DateTime? value)
    => value is null ? null : DateOnly.FromDateTime(value.Value);
}

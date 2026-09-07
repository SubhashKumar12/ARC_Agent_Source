using ARC.Domain.ValueObjects;

namespace ARC.Domain.Odos;

/// <summary>
/// Provider-agnostic shape of ODOS opening outstanding facts.
/// Production populates these fields from Oracle-synced <c>ODOS.odos_opening_data</c>
/// (columns od_company … od_os_amt_updt). Ageing buckets are Oracle-supplied —
/// consumers must not recalculate ageing from dates.
/// </summary>
public sealed record OdosOpeningSnapshot(
    string Company,
    int Year,
    int Month,
    string DepotCode,
    string DealerCode,
    string? BusinessLine,
    string? CustomerType,
    string? BillTo,
    string TrxId,
    DateOnly TrxDate,
    string DocType,
    string DocNo,
    decimal OsAmt0,
    decimal OsAmt1,
    decimal OsAmt2,
    decimal OsAmt3,
    decimal OsAmt4,
    decimal OsAmtUpdt,
    string Currency = "INR")
{
    /// <summary>Oracle-supplied total outstanding used for eligibility and gross AR projection.</summary>
    public Money OutstandingTotal => new(OsAmtUpdt, Currency);

    public string PeriodKey => $"{Year:D4}-{Month:D2}";
}

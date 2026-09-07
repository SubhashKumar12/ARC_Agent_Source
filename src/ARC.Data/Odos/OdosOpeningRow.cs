using System.Globalization;
using ARC.Domain.Odos;

namespace ARC.Data.Odos;

/// <summary>
/// Database-specific result shape returned by the approved opening-data stored procedure.
/// Column names match ODOS.odos_opening_data exactly. Mapped to the domain snapshot —
/// Dapper dynamic objects are never handed to Domain.
/// </summary>
internal sealed class OdosOpeningRow
{
    public string? od_company { get; set; }
    public string? od_year { get; set; }
    public string? od_month { get; set; }
    public string? od_depot_code { get; set; }
    public string? od_dealer_code { get; set; }
    public string? od_sbl_type { get; set; }
    public string? od_cust_type { get; set; }
    public string? od_bill_to { get; set; }
    public string? od_trx_id { get; set; }
    public DateTime? od_trx_date { get; set; }
    public string? od_doc_type { get; set; }
    public string? od_doc_no { get; set; }
    public decimal? od_os_amt0 { get; set; }
    public decimal? od_os_amt1 { get; set; }
    public decimal? od_os_amt2 { get; set; }
    public decimal? od_os_amt3 { get; set; }
    public decimal? od_os_amt4 { get; set; }
    public decimal? od_os_amt_updt { get; set; }

    /// <summary>
    /// Maps to the domain snapshot. Ageing buckets are carried through as supplied and
    /// <c>od_os_amt_updt</c> is retained exactly — no outstanding is derived or recomputed.
    /// </summary>
    public OdosOpeningSnapshot ToDomain(int fallbackYear, int fallbackMonth) => new(
        Company: od_company?.Trim() ?? string.Empty,
        Year: ParseInt(od_year) ?? fallbackYear,
        Month: ParseInt(od_month) ?? fallbackMonth,
        DepotCode: od_depot_code?.Trim() ?? string.Empty,
        DealerCode: od_dealer_code?.Trim() ?? string.Empty,
        BusinessLine: Nullable(od_sbl_type),
        CustomerType: Nullable(od_cust_type),
        BillTo: Nullable(od_bill_to),
        TrxId: od_trx_id?.Trim() ?? string.Empty,
        TrxDate: od_trx_date is { } d
            ? DateOnly.FromDateTime(d)
            : new DateOnly(ParseInt(od_year) ?? fallbackYear, ParseInt(od_month) ?? fallbackMonth, 1),
        DocType: od_doc_type?.Trim() ?? string.Empty,
        DocNo: od_doc_no?.Trim() ?? string.Empty,
        OsAmt0: od_os_amt0 ?? 0m,
        OsAmt1: od_os_amt1 ?? 0m,
        OsAmt2: od_os_amt2 ?? 0m,
        OsAmt3: od_os_amt3 ?? 0m,
        OsAmt4: od_os_amt4 ?? 0m,
        OsAmtUpdt: od_os_amt_updt ?? 0m);

    private static string? Nullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ParseInt(string? value)
        => int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}

/// <summary>Result shape of the approved business-limit stored procedure.</summary>
internal sealed class OdosBusinessLimitRow
{
    public string? business_line { get; set; }
    public decimal? business_limit { get; set; }

    public OdosBusinessLineLimit ToDomain() => new(
        string.IsNullOrWhiteSpace(business_line) ? null : business_line.Trim(),
        business_limit ?? 0m);
}

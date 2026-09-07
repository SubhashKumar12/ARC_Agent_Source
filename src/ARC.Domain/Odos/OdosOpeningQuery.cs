namespace ARC.Domain.Odos;

/// <summary>
/// Explicit ODOS opening-data read request. Mirrors the approved stored procedure parameters
/// (@Year, @Month, @DepotCode, @DealerCode) without naming the procedure.
/// Period is always explicit — ARC never infers a production eligibility period.
/// </summary>
public sealed record OdosOpeningQuery
{
    public OdosOpeningQuery(int year, int month, string? depotCode = null, string? dealerCode = null)
    {
        if (year is < 2000 or > 2999)
            throw new ArgumentOutOfRangeException(nameof(year), "Year must be a four digit calendar year.");
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");

        Year = year;
        Month = month;
        DepotCode = string.IsNullOrWhiteSpace(depotCode) ? null : depotCode.Trim();
        DealerCode = string.IsNullOrWhiteSpace(dealerCode) ? null : dealerCode.Trim();
    }

    public int Year { get; }
    public int Month { get; }
    public string? DepotCode { get; }
    public string? DealerCode { get; }

    /// <summary>@Year is VARCHAR(4) on the approved procedure.</summary>
    public string YearParameter => Year.ToString("D4");

    /// <summary>@Month is VARCHAR(2) on the approved procedure.</summary>
    public string MonthParameter => Month.ToString("D2");
}

/// <summary>
/// Period-explicit ODOS opening source. Implemented by the production stored-procedure adapter.
/// Separate from <see cref="IOdosOpeningDataSource"/> because production reads are period-scoped.
/// </summary>
public interface IOdosOpeningQuerySource
{
    Task<IReadOnlyList<OdosOpeningSnapshot>> QueryAsync(
        OdosOpeningQuery query,
        CancellationToken cancellationToken);
}

using Microsoft.Extensions.Options;
using ARC.Data.Odos;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.Odos;

namespace ARC.Data.Tests.Odos;

public sealed class OdosSqlBindingTests
{
    private const string OpeningProcedure = "ODOS.usp_GetOpeningDataForArc";
    private const string LimitProcedure = "ODOS.usp_GetBusinessLineLimit";

    private static SqlStoredProcedureNames Names() => new()
    {
        GetOdosOpeningData = OpeningProcedure,
        GetBusinessLineLimit = LimitProcedure
    };

    private static OdosOpeningRow SampleRow() => new()
    {
        od_company = "1",
        od_year = "2026",
        od_month = "07",
        od_depot_code = "101",
        od_dealer_code = "D0001",
        od_sbl_type = "DECO",
        od_cust_type = "DLR",
        od_bill_to = "BT-1",
        od_trx_id = "TRX-1",
        od_trx_date = new DateTime(2026, 7, 15),
        od_doc_type = "INV",
        od_doc_no = "DOC-1",
        od_os_amt0 = 1_000m,
        od_os_amt1 = 2_000m,
        od_os_amt2 = 3_000m,
        od_os_amt3 = 4_000m,
        od_os_amt4 = 5_000m,
        od_os_amt_updt = 12_345.67m
    };

    [Fact]
    public async Task OpeningSource_ResolvesProcedureNameFromOptions()
    {
        var executor = new RecordingStoredProcedureExecutor();
        var source = new SqlOdosOpeningQuerySource(executor, Options.Create(Names()));

        await source.QueryAsync(new OdosOpeningQuery(2026, 7), CancellationToken.None);

        Assert.Single(executor.ExecutedProcedures);
        Assert.Equal(OpeningProcedure, executor.ExecutedProcedures[0]);
    }

    [Fact]
    public async Task OpeningSource_PassesVarcharYearAndMonthParameters()
    {
        var executor = new RecordingStoredProcedureExecutor();
        var source = new SqlOdosOpeningQuerySource(executor, Options.Create(Names()));

        await source.QueryAsync(new OdosOpeningQuery(2026, 7, "101", "D0001"), CancellationToken.None);

        var parameters = executor.ExecutedParameters[0]!;
        var type = parameters.GetType();
        Assert.Equal("2026", type.GetProperty("Year")!.GetValue(parameters));
        Assert.Equal("07", type.GetProperty("Month")!.GetValue(parameters));
        Assert.Equal("101", type.GetProperty("DepotCode")!.GetValue(parameters));
        Assert.Equal("D0001", type.GetProperty("DealerCode")!.GetValue(parameters));
    }

    [Fact]
    public async Task OpeningSource_MapsAllOdosColumns()
    {
        var executor = new RecordingStoredProcedureExecutor();
        executor.SetResult<OdosOpeningRow>([SampleRow()]);
        var source = new SqlOdosOpeningQuerySource(executor, Options.Create(Names()));

        var rows = await source.QueryAsync(new OdosOpeningQuery(2026, 7), CancellationToken.None);

        var snapshot = Assert.Single(rows);
        Assert.Equal("1", snapshot.Company);
        Assert.Equal(2026, snapshot.Year);
        Assert.Equal(7, snapshot.Month);
        Assert.Equal("101", snapshot.DepotCode);
        Assert.Equal("D0001", snapshot.DealerCode);
        Assert.Equal("DECO", snapshot.BusinessLine);
        Assert.Equal("DLR", snapshot.CustomerType);
        Assert.Equal("BT-1", snapshot.BillTo);
        Assert.Equal("TRX-1", snapshot.TrxId);
        Assert.Equal(new DateOnly(2026, 7, 15), snapshot.TrxDate);
        Assert.Equal("INV", snapshot.DocType);
        Assert.Equal("DOC-1", snapshot.DocNo);
        Assert.Equal(1_000m, snapshot.OsAmt0);
        Assert.Equal(2_000m, snapshot.OsAmt1);
        Assert.Equal(3_000m, snapshot.OsAmt2);
        Assert.Equal(4_000m, snapshot.OsAmt3);
        Assert.Equal(5_000m, snapshot.OsAmt4);
        Assert.Equal(12_345.67m, snapshot.OsAmtUpdt);
    }

    [Fact]
    public async Task OpeningSource_RetainsOsAmtUpdtExactly_WithoutDerivingFromBuckets()
    {
        var row = SampleRow();
        // Bucket total (15,000) deliberately differs from od_os_amt_updt (12,345.67).
        var executor = new RecordingStoredProcedureExecutor();
        executor.SetResult<OdosOpeningRow>([row]);
        var source = new SqlOdosOpeningQuerySource(executor, Options.Create(Names()));

        var snapshot = (await source.QueryAsync(new OdosOpeningQuery(2026, 7), CancellationToken.None))[0];

        var bucketTotal = snapshot.OsAmt0 + snapshot.OsAmt1 + snapshot.OsAmt2 + snapshot.OsAmt3 + snapshot.OsAmt4;
        Assert.Equal(15_000m, bucketTotal);
        Assert.Equal(12_345.67m, snapshot.OsAmtUpdt);
        Assert.Equal(12_345.67m, snapshot.OutstandingTotal.Amount);
        Assert.NotEqual(bucketTotal, snapshot.OsAmtUpdt);
    }

    [Fact]
    public async Task OpeningSource_DoesNotRecalculateAgeingFromTrxDate()
    {
        var row = SampleRow();
        // A very old transaction date must not shift amounts between buckets.
        row.od_trx_date = new DateTime(2019, 1, 1);
        var executor = new RecordingStoredProcedureExecutor();
        executor.SetResult<OdosOpeningRow>([row]);
        var source = new SqlOdosOpeningQuerySource(executor, Options.Create(Names()));

        var snapshot = (await source.QueryAsync(new OdosOpeningQuery(2026, 7), CancellationToken.None))[0];

        Assert.Equal(1_000m, snapshot.OsAmt0);
        Assert.Equal(2_000m, snapshot.OsAmt1);
        Assert.Equal(3_000m, snapshot.OsAmt2);
        Assert.Equal(4_000m, snapshot.OsAmt3);
        Assert.Equal(5_000m, snapshot.OsAmt4);
    }

    [Fact]
    public async Task LimitSource_ResolvesProcedureNameFromOptions()
    {
        var executor = new RecordingStoredProcedureExecutor();
        var source = new SqlOdosBusinessLineLimitSource(executor, Options.Create(Names()));

        await source.GetLimitAsync("DECO", CancellationToken.None);

        Assert.Equal(LimitProcedure, Assert.Single(executor.ExecutedProcedures));
    }

    [Fact]
    public async Task LimitSource_MapsBusinessLineAndLimitColumns()
    {
        var executor = new RecordingStoredProcedureExecutor();
        executor.SetSingle(new OdosBusinessLimitRow { business_line = "DECO", business_limit = 25_000m });
        var source = new SqlOdosBusinessLineLimitSource(executor, Options.Create(Names()));

        var limit = await source.GetLimitAsync("DECO", CancellationToken.None);

        Assert.NotNull(limit);
        Assert.Equal("DECO", limit!.BusinessLine);
        Assert.Equal(25_000m, limit.BusinessLimit);
    }

    [Fact]
    public async Task LimitProvider_UsesSpDefault_WhenBusinessLineUnknown()
    {
        var source = new StubLimitSource(defaultLimit: 9_500m);

        var provider = await PrefetchedOdosBusinessLineLimitProvider.CreateAsync(
            source,
            ["DECO"],
            CancellationToken.None);

        Assert.Equal(9_500m, provider.GetDefaultLimit());
        Assert.Null(provider.GetBusinessLineLimit("UNKNOWN_LINE"));
        Assert.Equal(11_000m, provider.GetBusinessLineLimit("DECO"));
    }

    [Fact]
    public async Task Eligibility_UsesOutstandingGreaterThanLimit()
    {
        var provider = await PrefetchedOdosBusinessLineLimitProvider.CreateAsync(
            new StubLimitSource(defaultLimit: 10_000m),
            ["DECO"],
            CancellationToken.None);

        // DECO limit from stub is 11,000.
        Assert.True(OdosEligibility.ExceedsOutstandingLimit(11_000.01m, "DECO", provider));
        Assert.False(OdosEligibility.ExceedsOutstandingLimit(10_999.99m, "DECO", provider));
    }

    [Fact]
    public async Task Eligibility_EqualityIsNotEligible()
    {
        var provider = await PrefetchedOdosBusinessLineLimitProvider.CreateAsync(
            new StubLimitSource(defaultLimit: 10_000m),
            ["DECO"],
            CancellationToken.None);

        Assert.False(OdosEligibility.ExceedsOutstandingLimit(11_000m, "DECO", provider));
        Assert.False(OdosEligibility.ExceedsOutstandingLimit(10_000m, null, provider));
        Assert.True(OdosEligibility.ExceedsOutstandingLimit(10_000.01m, null, provider));
    }

    [Fact]
    public async Task Eligibility_IsNeverHardCodedTo5000()
    {
        var provider = await PrefetchedOdosBusinessLineLimitProvider.CreateAsync(
            new StubLimitSource(defaultLimit: 40_000m),
            [],
            CancellationToken.None);

        Assert.Equal(40_000m, provider.GetDefaultLimit());
        Assert.False(OdosEligibility.ExceedsOutstandingLimit(6_000m, null, provider));
    }

    private sealed class StubLimitSource : IOdosBusinessLineLimitSource
    {
        private readonly decimal _defaultLimit;

        public StubLimitSource(decimal defaultLimit) => _defaultLimit = defaultLimit;

        public Task<OdosBusinessLineLimit?> GetLimitAsync(string? businessLine, CancellationToken cancellationToken)
            => Task.FromResult<OdosBusinessLineLimit?>(
                businessLine is null
                    ? new OdosBusinessLineLimit(null, _defaultLimit)
                    : new OdosBusinessLineLimit(businessLine, 11_000m));
    }
}

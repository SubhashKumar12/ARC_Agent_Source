using ARC.Data.Odos;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.Odos;
using Microsoft.Extensions.Options;

namespace ARC.Data.Tests.Odos;

public sealed class SqlOdosOutstandingDetailReaderTests
{
    private const string HeaderProcedure = "ODOS.usp_GetDealerDetails";

    [Fact]
    public async Task Header_business_line_limit_is_mapped_without_limit_sp_fallback()
    {
        var executor = new RecordingStoredProcedureExecutor();
        executor.SetSingle(new OdosRecoveryOutstandingRow
        {
            os_amt_updt = 7_225_124.13m,
            os_amt0 = -125_000m,
            os_amt1 = 0m,
            os_amt2 = 81_403.68m,
            os_amt3 = 7_268_720.45m,
            os_amt4 = 0m,
            business_line_limit = 5_000m,
            tsi_visit_status = "No",
            tsi_visit_count = 0,
            dealer_feedback = "",
            demand_notice_generated_yn = "Y",
            current_status_code = "00",
            current_status = "New",
            legal_status_code = "00",
            legal_status = "New"
        });

        var limitSource = new TrackingBusinessLineLimitSource();
        var reader = CreateReader(executor, limitSource);

        var detail = await reader.GetByOdosKeysAsync("020", "115014", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(5_000m, detail!.BusinessLineLimit);
        Assert.Equal("No", detail.TsiVisitStatus);
        Assert.Equal(0, detail.TsiVisitCount);
        Assert.Equal("", detail.DealerFeedback);
        Assert.DoesNotContain("usp_GetBusinessLineLimit", executor.ExecutedProcedures, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, limitSource.CallCount);
    }

    [Fact]
    public async Task Limit_sp_is_used_only_when_header_limit_is_null()
    {
        var executor = new RecordingStoredProcedureExecutor();
        executor.SetSingle(new OdosRecoveryOutstandingRow
        {
            os_amt_updt = 100m,
            dlr_sbl = "DECO",
            business_line_limit = null
        });

        var limitSource = new TrackingBusinessLineLimitSource { Limit = 5_000m };
        var reader = CreateReader(executor, limitSource);

        var detail = await reader.GetByOdosKeysAsync("020", "115014", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(5_000m, detail!.BusinessLineLimit);
        Assert.Equal(1, limitSource.CallCount);
    }

    private static SqlOdosOutstandingDetailReader CreateReader(
        RecordingStoredProcedureExecutor executor,
        TrackingBusinessLineLimitSource limitSource)
    {
        var session = Options.Create(new OdosSqlSessionOptions
        {
            CommtYear = "2026",
            CommtMonth = "07",
            UserId = "murthy"
        });
        var spNames = Options.Create(new SqlStoredProcedureNames
        {
            GetLegalCase = HeaderProcedure,
            GetOdosOpeningData = "ODOS.usp_GetOpeningDataForArc",
            GetBusinessLineLimit = "ODOS.usp_GetBusinessLineLimit"
        });
        var opening = new EmptyOdosOpeningQuerySource();
        return new SqlOdosOutstandingDetailReader(executor, opening, limitSource, session, spNames);
    }

    private sealed class TrackingBusinessLineLimitSource : IOdosBusinessLineLimitSource
    {
        public decimal? Limit { get; init; }
        public int CallCount { get; private set; }

        public Task<OdosBusinessLineLimit?> GetLimitAsync(string? businessLine, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Limit is null
                ? null
                : new OdosBusinessLineLimit(businessLine, Limit.Value));
        }
    }

    private sealed class EmptyOdosOpeningQuerySource : IOdosOpeningQuerySource
    {
        public Task<IReadOnlyList<OdosOpeningSnapshot>> QueryAsync(
            OdosOpeningQuery query,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<OdosOpeningSnapshot>>([]);
    }
}

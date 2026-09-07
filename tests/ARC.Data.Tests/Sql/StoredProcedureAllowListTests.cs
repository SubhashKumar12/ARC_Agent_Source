using ARC.Data.Sql.StoredProcedures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ARC.Data.Tests.Sql;

public sealed class StoredProcedureAllowListTests
{
    [Fact]
    public async Task AllowList_RejectsUnknownProcedure()
    {
        var names = new SqlStoredProcedureNames();
        var options = new SqlStoredProcedureOptions();
        var fake = new FakeStoredProcedureExecutor(Options.Create(names));

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await fake.QueryAsync<object>("dbo.usp_ArbitraryAgentSuppliedProcedure"));

        Assert.Contains("not allow-listed", ex.Message);
    }

    [Fact]
    public async Task AllowList_AcceptsConfiguredProcedure()
    {
        var names = new SqlStoredProcedureNames
        {
            GetDealer = "dbo.usp_GetDealer"
        };
        var fake = new FakeStoredProcedureExecutor(Options.Create(names));

        var result = await fake.QuerySingleOrDefaultAsync<object>("dbo.usp_GetDealer");
        Assert.Null(result);
    }

    [Fact]
    public async Task AllowList_BuildsFromConfiguration()
    {
        var names = new SqlStoredProcedureNames
        {
            GetDealer = "dbo.usp_GetDealer",
            ListDealersByRegion = "dbo.usp_ListDealersByRegion"
        };
        var fake = new FakeStoredProcedureExecutor(Options.Create(names));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await fake.QueryAsync<object>("dbo.usp_NotInConfig"));
    }

    [Fact]
    public async Task AllowList_IsCaseInsensitive()
    {
        var names = new SqlStoredProcedureNames
        {
            GetDealer = "dbo.usp_GetDealer"
        };
        var fake = new FakeStoredProcedureExecutor(Options.Create(names));

        var result1 = await fake.QuerySingleOrDefaultAsync<object>("dbo.usp_GetDealer");
        var result2 = await fake.QuerySingleOrDefaultAsync<object>("dbo.USP_GETDEALER");
        var result3 = await fake.QuerySingleOrDefaultAsync<object>("DBO.usp_getdealer");

        Assert.Null(result1);
        Assert.Null(result2);
        Assert.Null(result3);
    }

    [Fact]
    public async Task FakeExecutor_ReturnsEmptyResults_WithoutDatabase()
    {
        var names = new SqlStoredProcedureNames
        {
            GetDealer = "dbo.usp_GetDealer",
            ListDealersByRegion = "dbo.usp_ListDealersByRegion"
        };
        var fake = new FakeStoredProcedureExecutor(Options.Create(names));

        var single = await fake.QuerySingleOrDefaultAsync<string>("dbo.usp_GetDealer");
        var list = await fake.QueryAsync<string>("dbo.usp_ListDealersByRegion");
        var affected = await fake.ExecuteAsync("dbo.usp_GetDealer");
        var scalar = await fake.ExecuteScalarAsync<int>("dbo.usp_GetDealer");

        Assert.Null(single);
        Assert.Empty(list);
        Assert.Equal(0, affected);
        Assert.Equal(0, scalar);
    }
}

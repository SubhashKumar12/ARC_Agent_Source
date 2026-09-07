using Microsoft.Extensions.Options;
using ARC.Data.Odos;
using ARC.Data.Sql;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Tests.Odos;

public sealed class Phase13C_DealerRepositoryTests
{
    private const string DealerProcedure = "ODOS.usp_ARC_GetDealer";
    private const string Urn = "dealer:13c:master";
    private const string Depot = "101";
    private const string Dealer = "8778";

    private static SqlStoredProcedureNames Names() => new()
    {
        GetDealer = DealerProcedure
    };

    private static DealerRepository CreateRepository(
        RecordingStoredProcedureExecutor executor,
        OdosDealerKeyOptions? keys = null)
    {
        keys ??= new OdosDealerKeyOptions
        {
            ByUrn = { [Urn] = new OdosDealerKeyEntry { DepotCode = Depot, DealerCode = Dealer } }
        };

        return new DealerRepository(
            executor,
            new StubConnectionFactory(),
            new OdosDealerKeyResolver(Options.Create(keys)),
            Options.Create(Names()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DealerRepository>.Instance);
    }

    [Fact]
    public async Task GetAsync_UsesConfiguredOdosProcedure()
    {
        var executor = new RecordingStoredProcedureExecutor();
        executor.SetSingle(CreateRow());

        var repo = CreateRepository(executor);
        await repo.GetAsync(new DealerUrn(Urn), CancellationToken.None);

        Assert.Equal(DealerProcedure, Assert.Single(executor.ExecutedProcedures));
    }

    [Fact]
    public async Task GetAsync_PassesDepotCodeAndDealerCodeParameters()
    {
        var executor = new RecordingStoredProcedureExecutor();
        executor.SetSingle(CreateRow());

        var repo = CreateRepository(executor);
        await repo.GetAsync(new DealerUrn(Urn), CancellationToken.None);

        var parameters = executor.ExecutedParameters[0]!;
        var type = parameters.GetType();
        Assert.Equal(Depot, type.GetProperty("DepotCode")!.GetValue(parameters));
        Assert.Equal(Dealer, type.GetProperty("DealerCode")!.GetValue(parameters));
    }

    [Fact]
    public async Task GetAsync_ResolvesDealerUrnViaOdosKeyResolver()
    {
        var executor = new RecordingStoredProcedureExecutor();
        executor.SetSingle(CreateRow());

        var repo = CreateRepository(executor);
        var dealer = await repo.GetAsync(new DealerUrn(Urn), CancellationToken.None);

        Assert.NotNull(dealer);
        Assert.Equal(Depot, dealer.Depot);
        Assert.Equal(Dealer, executor.ExecutedParameters[0]!.GetType().GetProperty("DealerCode")!.GetValue(executor.ExecutedParameters[0]));
    }

    [Fact]
    public async Task GetAsync_FailsClosedWhenOdosKeyMissing()
    {
        var executor = new RecordingStoredProcedureExecutor();
        var repo = CreateRepository(executor, new OdosDealerKeyOptions());

        await Assert.ThrowsAsync<ARC.Data.Exceptions.DataAccessException>(
            () => repo.GetAsync(new DealerUrn(Urn), CancellationToken.None));

        Assert.Empty(executor.ExecutedProcedures);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWhenProcedureReturnsNoRow()
    {
        var executor = new RecordingStoredProcedureExecutor();
        var repo = CreateRepository(executor);

        var dealer = await repo.GetAsync(new DealerUrn(Urn), CancellationToken.None);
        Assert.Null(dealer);
    }

    [Fact]
    public async Task GetAsync_DoesNotFabricateUnsupportedDealerFields()
    {
        var executor = new RecordingStoredProcedureExecutor();
        executor.SetSingle(CreateRow(billTo: "BT-999", terrName: "North Territory"));

        var repo = CreateRepository(executor);
        var dealer = await repo.GetAsync(new DealerUrn(Urn), CancellationToken.None);

        Assert.NotNull(dealer);
        Assert.Null(dealer.SapCode);
        Assert.Null(dealer.PortalId);
        Assert.Null(dealer.CoveringTsi);
        Assert.Null(dealer.AppId);
        Assert.False(dealer.UnderInsolvencyMoratorium);
    }

    [Fact]
    public async Task GetMasterDetailAsync_DoesNotExposeDealerMobile()
    {
        var executor = new RecordingStoredProcedureExecutor();
        executor.SetSingle(CreateRow(mobile: "9999999999"));

        var repo = CreateRepository(executor);
        var detail = await repo.GetMasterDetailAsync(new DealerUrn(Urn), CancellationToken.None);

        Assert.NotNull(detail);
        var json = System.Text.Json.JsonSerializer.Serialize(detail);
        Assert.DoesNotContain("9999999999", json, StringComparison.Ordinal);
        Assert.DoesNotContain("dealer_mobile", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DealerMobile", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowList_RejectsArbitraryProcedureNameForDealerRead()
    {
        var names = Names();
        var fake = new FakeStoredProcedureExecutor(Options.Create(names));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await fake.QuerySingleOrDefaultAsync<ArcDealerMasterRow>("dbo.usp_ArbitraryInjectedProcedure"));
    }

    private static ArcDealerMasterRow CreateRow(
        string? billTo = "BT-1",
        string? terrName = "Territory A",
        string? mobile = null)
        => new()
        {
            depot_code = Depot,
            dealer_code = Dealer,
            dealer_name = "Test Dealer",
            depot_name = "Test Depot",
            depot_regn = "N1",
            region = "North",
            terr_code = "T01",
            terr_name = terrName,
            sbl_code = "SBL1",
            gold_silver = "G",
            bill_to = billTo,
            primary_flag = "Y",
            cust_type = "D",
            mother_acc = Dealer,
            dealer_mobile = mobile
        };

    private sealed class StubConnectionFactory : ISqlConnectionFactory
    {
        public Task<Microsoft.Data.SqlClient.SqlConnection> OpenAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("List methods not used in these tests.");
    }
}

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ARC.Data.Odos;
using ARC.Data.Sql;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Tests.Odos;

public sealed class Phase12C_ChequeRepositoryTests
{
    private const string ChequeProcedure = "ODOS.usp_GetSubmittedChequesForDealerODOS";
    private const string Urn = "dealer:12c:cheque";
    private const string Depot = "101";
    private const string Dealer = "D00012";

    private static SqlStoredProcedureNames Names() => new()
    {
        ListSecurityChequesByDealer = ChequeProcedure
    };

    private static ChequeRepository CreateRepository(
        RecordingStoredProcedureExecutor executor,
        OdosDealerKeyOptions? keys = null)
    {
        keys ??= new OdosDealerKeyOptions
        {
            ByUrn = { [Urn] = new OdosDealerKeyEntry { DepotCode = Depot, DealerCode = Dealer } }
        };

        return new ChequeRepository(
            executor,
            new StubConnectionFactory(),
            new OdosDealerKeyResolver(Options.Create(keys)),
            Options.Create(Names()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ChequeRepository>.Instance);
    }

    [Fact]
    public async Task ListCheques_UsesConfiguredOdosProcedure()
    {
        var executor = new RecordingStoredProcedureExecutor();
        executor.SetResult<OdosSubmittedChequeRow>(
        [
            new OdosSubmittedChequeRow
            {
                cheque_no = "123456",
                amount = 50_000m,
                cheque_status = "BOUNCED",
                bounce_date = new DateTime(2026, 3, 1)
            }
        ]);

        var repo = CreateRepository(executor);
        await repo.ListChequesAsync(new DealerUrn(Urn), CancellationToken.None);

        Assert.Equal(ChequeProcedure, Assert.Single(executor.ExecutedProcedures));
    }

    [Fact]
    public async Task ListCheques_MapsSupportedFieldsWithoutInventingValidityEnd()
    {
        var executor = new RecordingStoredProcedureExecutor();
        executor.SetResult<OdosSubmittedChequeRow>(
        [
            new OdosSubmittedChequeRow
            {
                cheque_no = "9001",
                amount = 100_000m,
                cheque_status = "BOUNCED",
                bounce_date = new DateTime(2026, 2, 15),
                ifsc_code = "HDFC0001234",
                cheque_date = new DateTime(2025, 12, 1),
                section_138_eligible_yn = "Y"
            }
        ]);

        var repo = CreateRepository(executor);
        var cheques = await repo.ListChequesAsync(new DealerUrn(Urn), CancellationToken.None);
        var cheque = Assert.Single(cheques);

        Assert.Equal("9001", cheque.ChequeNumber);
        Assert.Equal(100_000m, cheque.Amount.Amount);
        Assert.Equal(ChequeStatus.Bounced, cheque.Status);
        Assert.Null(cheque.ValidityEnd);
        Assert.Null(cheque.Micr);
        Assert.Null(cheque.DepositDate);
        Assert.Null(cheque.ExtractionConfidence);
    }

    [Fact]
    public async Task ListCheques_MapsRealizedStatus()
    {
        var executor = new RecordingStoredProcedureExecutor();
        executor.SetResult<OdosSubmittedChequeRow>(
        [
            new OdosSubmittedChequeRow
            {
                cheque_no = "7777",
                amount = 25_000m,
                cheque_status = "REALIZED"
            }
        ]);

        var repo = CreateRepository(executor);
        var cheque = Assert.Single(await repo.ListChequesAsync(new DealerUrn(Urn), CancellationToken.None));
        Assert.Equal(ChequeStatus.Realised, cheque.Status);
    }

    [Fact]
    public async Task ListCheques_SkipsUnsupportedChequeStatusesWithoutManufacturingReturnReason()
    {
        var executor = new RecordingStoredProcedureExecutor();
        executor.SetResult<OdosSubmittedChequeRow>(
        [
            new() { cheque_no = "1", amount = 1m, cheque_status = "PENDING_HO" },
            new() { cheque_no = "2", amount = 2m, cheque_status = "BOUNCED" }
        ]);

        var repo = CreateRepository(executor);
        var cheques = await repo.ListChequesAsync(new DealerUrn(Urn), CancellationToken.None);

        var cheque = Assert.Single(cheques);
        Assert.Equal("2", cheque.ChequeNumber);
    }

    [Fact]
    public async Task ListCheques_PassesDepotAndDealerParameters()
    {
        var executor = new RecordingStoredProcedureExecutor();
        var repo = CreateRepository(executor);
        await repo.ListChequesAsync(new DealerUrn(Urn), CancellationToken.None);

        var parameters = executor.ExecutedParameters[0]!;
        var type = parameters.GetType();
        Assert.Equal(Depot, type.GetProperty("depot_code")!.GetValue(parameters));
        Assert.Equal(Dealer, type.GetProperty("dealer_code")!.GetValue(parameters));
    }

    [Fact]
    public async Task ListCheques_FailsClosedWhenOdosKeyMissing()
    {
        var executor = new RecordingStoredProcedureExecutor();
        var repo = CreateRepository(executor, new OdosDealerKeyOptions());

        await Assert.ThrowsAsync<ARC.Data.Exceptions.DataAccessException>(
            () => repo.ListChequesAsync(new DealerUrn(Urn), CancellationToken.None));
    }

    private sealed class StubConnectionFactory : ISqlConnectionFactory
    {
        public Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("Return memo path not used in these tests.");
    }
}

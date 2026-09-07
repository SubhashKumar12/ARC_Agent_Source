using Microsoft.Extensions.Options;
using ARC.Data.Odos;
using ARC.Data.Sql.StoredProcedures;
using ARC.Data.Synthetic;
using ARC.Domain.Odos;

namespace ARC.Data.Tests.Odos;

public sealed class OdosDryRunAndArchitectureTests
{
    [Fact]
    public async Task DryRun_ReportsEligibilityAgainstSpSuppliedLimit()
    {
        var opening = new StubOpeningSource(
            Row("101", "D0001", "DECO", outstanding: 50_000m),
            Row("101", "D0002", "DECO", outstanding: 5_000m));

        var service = new OdosDryRunService(opening, new StubLimitSource(11_000m));
        var result = await service.RunAsync(new OdosOpeningQuery(2026, 7), 10, CancellationToken.None);

        Assert.Equal(2, result.RowsReturned);
        Assert.Equal(2, result.SampleSize);
        Assert.Equal(2026, result.Year);
        Assert.Equal(7, result.Month);

        var eligible = result.Sample.Single(l => l.DealerCode == "D0001");
        var notEligible = result.Sample.Single(l => l.DealerCode == "D0002");

        Assert.True(eligible.EligibleByCurrentOdosLimit);
        Assert.False(notEligible.EligibleByCurrentOdosLimit);
        Assert.Equal(11_000m, eligible.BusinessLimit);
    }

    [Fact]
    public async Task DryRun_LimitsSampleSize()
    {
        var rows = Enumerable.Range(1, 25)
            .Select(i => Row("101", $"D{i:D4}", "DECO", 20_000m))
            .ToArray();

        var service = new OdosDryRunService(new StubOpeningSource(rows), new StubLimitSource(11_000m));
        var result = await service.RunAsync(new OdosOpeningQuery(2026, 7), 5, CancellationToken.None);

        Assert.Equal(25, result.RowsReturned);
        Assert.Equal(5, result.SampleSize);
        Assert.Equal(5, result.Sample.Count);
    }

    [Fact]
    public async Task DryRun_CarriesBucketsThroughUnchanged()
    {
        var service = new OdosDryRunService(
            new StubOpeningSource(Row("101", "D0001", "DECO", 12_345.67m)),
            new StubLimitSource(11_000m));

        var line = (await service.RunAsync(new OdosOpeningQuery(2026, 7), 1, CancellationToken.None)).Sample[0];

        Assert.Equal(12_345.67m, line.CurrentOutstanding);
        Assert.Equal(1_000m, line.OsAmt0);
        Assert.Equal(2_000m, line.OsAmt1);
        Assert.Equal(3_000m, line.OsAmt2);
        Assert.Equal(4_000m, line.OsAmt3);
        Assert.Equal(5_000m, line.OsAmt4);
    }

    [Fact]
    public void OpeningQuery_RequiresValidExplicitPeriod()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OdosOpeningQuery(2026, 13));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OdosOpeningQuery(2026, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OdosOpeningQuery(1999, 7));

        var query = new OdosOpeningQuery(2026, 7);
        Assert.Equal("2026", query.YearParameter);
        Assert.Equal("07", query.MonthParameter);
        Assert.Null(query.DepotCode);
        Assert.Null(query.DealerCode);
    }

    [Fact]
    public async Task FakeStoredProcedureExecutor_StillEnforcesAllowList()
    {
        var names = new SqlStoredProcedureNames();
        var fake = new FakeStoredProcedureExecutor(Options.Create(names));

        var rows = await fake.QueryAsync<object>(names.GetOdosOpeningData);
        Assert.Empty(rows);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await fake.QueryAsync<object>("ODOS.usp_SomethingNotApproved"));
    }

    [Fact]
    public void SyntheticPath_RemainsIndependentOfSqlBinding()
    {
        var store = SyntheticDatasetStore.CreateDefault(dealerCount: 25);

        Assert.Equal(SyntheticAssignmentLabels.Marker, store.Dataset.DataClassification);
        Assert.Equal(12, store.Dataset.HistoryMonthCount);
        Assert.True(store.Dataset.R6LineageCase.ReconciledNetExposure > 0m);
    }

    [Fact]
    public void OdosSources_DoNotCreateSqlConnectionsDirectly()
    {
        var files = new[]
        {
            "SqlOdosOpeningQuerySource.cs",
            "SqlOdosBusinessLineLimitSource.cs",
            "OdosDryRunService.cs"
        };

        var odosPath = Path.Combine(SolutionRoot(), "src", "ARC.Data", "Odos");
        foreach (var file in files)
        {
            var content = File.ReadAllText(Path.Combine(odosPath, file));
            Assert.DoesNotContain("new SqlConnection", content);
            Assert.DoesNotContain("SqlConnectionStringBuilder", content);
            Assert.DoesNotContain("ConnectionString", content);
        }
    }

    [Fact]
    public void OdosSources_ContainNoInlineSqlOrLiteralProcedureNames()
    {
        var odosPath = Path.Combine(SolutionRoot(), "src", "ARC.Data", "Odos");
        foreach (var file in new[] { "SqlOdosOpeningQuerySource.cs", "SqlOdosBusinessLineLimitSource.cs" })
        {
            var content = File.ReadAllText(Path.Combine(odosPath, file));

            Assert.DoesNotContain("\"SELECT", content);
            Assert.DoesNotContain("\"INSERT", content);
            Assert.DoesNotContain("\"UPDATE", content);
            Assert.DoesNotContain("\"DELETE", content);
            Assert.DoesNotContain("\"MERGE", content);

            // Procedure names must come from configuration, never as literals here.
            Assert.DoesNotContain("\"ODOS.usp_", content);
        }
    }

    [Fact]
    public void ConnectionFactory_IsTheOnlyPlaceBuildingConnectionStrings()
    {
        var sqlPath = Path.Combine(SolutionRoot(), "src", "ARC.Data");
        var offenders = Directory
            .GetFiles(sqlPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("new SqlConnection("))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(["SqlConnectionFactory.cs"], offenders);
    }

    [Fact]
    public void ConnectionOptions_DoNotLogOrExposePassword()
    {
        var path = Path.Combine(SolutionRoot(), "src", "ARC.Data", "Configuration", "OdosSqlConnectionOptions.cs");
        var content = File.ReadAllText(path);

        Assert.DoesNotContain("LogInformation", content);
        Assert.DoesNotContain("Console.Write", content);

        var factory = File.ReadAllText(
            Path.Combine(SolutionRoot(), "src", "ARC.Data", "Sql", "SqlConnectionFactory.cs"));

        // Logging must reference the safe descriptor, never the password or raw connection string.
        Assert.Contains("SafeDescriptor", factory);
        Assert.DoesNotContain("DBServerPassword}", factory);
        Assert.DoesNotContain("{connectionString}", factory);
    }

    private static OdosOpeningSnapshot Row(string depot, string dealer, string businessLine, decimal outstanding)
        => new(
            Company: "1",
            Year: 2026,
            Month: 7,
            DepotCode: depot,
            DealerCode: dealer,
            BusinessLine: businessLine,
            CustomerType: "DLR",
            BillTo: "BT",
            TrxId: "TRX",
            TrxDate: new DateOnly(2026, 7, 15),
            DocType: "INV",
            DocNo: "DOC",
            OsAmt0: 1_000m,
            OsAmt1: 2_000m,
            OsAmt2: 3_000m,
            OsAmt3: 4_000m,
            OsAmt4: 5_000m,
            OsAmtUpdt: outstanding);

    private static string SolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "ARC.sln")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new InvalidOperationException("Solution root not found");
    }

    private sealed class StubOpeningSource : IOdosOpeningQuerySource
    {
        private readonly IReadOnlyList<OdosOpeningSnapshot> _rows;

        public StubOpeningSource(params OdosOpeningSnapshot[] rows) => _rows = rows;

        public Task<IReadOnlyList<OdosOpeningSnapshot>> QueryAsync(
            OdosOpeningQuery query,
            CancellationToken cancellationToken)
            => Task.FromResult(_rows);
    }

    private sealed class StubLimitSource : IOdosBusinessLineLimitSource
    {
        private readonly decimal _lineLimit;

        public StubLimitSource(decimal lineLimit) => _lineLimit = lineLimit;

        public Task<OdosBusinessLineLimit?> GetLimitAsync(string? businessLine, CancellationToken cancellationToken)
            => Task.FromResult<OdosBusinessLineLimit?>(new OdosBusinessLineLimit(businessLine, _lineLimit));
    }
}

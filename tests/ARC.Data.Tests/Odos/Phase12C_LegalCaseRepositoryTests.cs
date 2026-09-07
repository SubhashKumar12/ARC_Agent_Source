using Microsoft.Extensions.Options;
using ARC.Data.Odos;
using ARC.Data.Sql;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Tests.Odos;

public sealed class Phase12C_LegalCaseRepositoryTests
{
    private const string HeaderProcedure = "ODOS.usp_GetDealerDetails";
    private const string DocumentsProcedure = "ODOS.usp_GetLegalDocumentsODOS";
    private const string Urn = "dealer:12c:legal";
    private const string Depot = "101";
    private const string Dealer = "D00012";

    private static SqlStoredProcedureNames Names() => new()
    {
        GetLegalCase = HeaderProcedure,
        GetLegalDocuments = DocumentsProcedure
    };

    private static OdosSqlSessionOptions Session() => new()
    {
        CommtYear = "2026",
        CommtMonth = "07",
        UserId = "arc.service"
    };

    private static LegalCaseRepository CreateRepository(MultiResultExecutor executor)
    {
        var keys = new OdosDealerKeyOptions
        {
            ByUrn = { [Urn] = new OdosDealerKeyEntry { DepotCode = Depot, DealerCode = Dealer } }
        };

        return new LegalCaseRepository(
            executor,
            connections: null,
            overlay: new NullOverlayReader(),
            odosKeys: new OdosDealerKeyResolver(Options.Create(keys)),
            session: Options.Create(Session()),
            spNames: Options.Create(Names()),
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<LegalCaseRepository>.Instance);
    }

    private sealed class NullOverlayReader : ILegalCaseCompletenessOverlayReader
    {
        public Task<LegalCaseCompletenessOverlay?> LoadAsync(DealerUrn urn, CancellationToken cancellationToken)
            => Task.FromResult<LegalCaseCompletenessOverlay?>(null);
    }

    [Fact]
    public async Task GetAsync_UsesOdosHeaderAndSection138Documents()
    {
        var executor = new MultiResultExecutor();
        executor.SetHeader(new OdosDealerDetailsRow
        {
            legal_id = 42,
            current_status_code = "04",
            legal_status_code = "04",
            os_amt_updt = 120_000m
        });
        executor.SetDocuments(
        [
            new OdosLegalDocumentRow { ldoc_id = 1, ldoc_category_code = "04", ldoc_file_name = "s138.pdf" }
        ]);

        var repo = CreateRepository(executor);
        var legalCase = await repo.GetAsync(new DealerUrn(Urn), CancellationToken.None);

        Assert.NotNull(legalCase);
        Assert.Equal("42", legalCase!.CaseReference);
        Assert.Equal(0m, legalCase.CompletenessScore);
        Assert.Empty(legalCase.Gaps);
        Assert.Equal([HeaderProcedure, DocumentsProcedure], executor.ExecutedProcedures);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWhenNoOdosHeader()
    {
        var executor = new MultiResultExecutor();
        executor.SetHeader(null);

        var repo = CreateRepository(executor);
        var legalCase = await repo.GetAsync(new DealerUrn(Urn), CancellationToken.None);

        Assert.Null(legalCase);
        Assert.Equal([HeaderProcedure], executor.ExecutedProcedures);
    }

    [Fact]
    public async Task GetAsync_DoesNotInventCourtFields()
    {
        var executor = new MultiResultExecutor();
        executor.SetHeader(new OdosDealerDetailsRow
        {
            legal_id = 99,
            current_status_code = "04"
        });
        executor.SetDocuments([]);

        var repo = CreateRepository(executor);
        var legalCase = await repo.GetAsync(new DealerUrn(Urn), CancellationToken.None);

        Assert.NotNull(legalCase);
        Assert.Equal("99", legalCase!.CaseReference);
        Assert.Equal(0m, legalCase.CompletenessScore);
    }

    [Fact]
    public async Task GetAsync_PassesSection138CategoryFilter()
    {
        var executor = new MultiResultExecutor();
        executor.SetHeader(new OdosDealerDetailsRow { legal_id = 7 });
        executor.SetDocuments([]);

        var repo = CreateRepository(executor);
        await repo.GetAsync(new DealerUrn(Urn), CancellationToken.None);

        var docParams = executor.ExecutedParameters[1]!;
        var type = docParams.GetType();
        Assert.Equal(7L, type.GetProperty("ldoc_legal_id")!.GetValue(docParams));
        Assert.Equal("04", type.GetProperty("ldoc_category_code")!.GetValue(docParams));
    }

    /// <summary>Executor that returns different results per procedure name.</summary>
    internal sealed class MultiResultExecutor : IStoredProcedureExecutor
    {
        private OdosDealerDetailsRow? _header;
        private IReadOnlyList<OdosLegalDocumentRow> _documents = [];

        public List<string> ExecutedProcedures { get; } = [];
        public List<object?> ExecutedParameters { get; } = [];

        public void SetHeader(OdosDealerDetailsRow? header) => _header = header;
        public void SetDocuments(IReadOnlyList<OdosLegalDocumentRow> documents) => _documents = documents;

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            string configuredProcedureName,
            object? parameters = null,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            Record(configuredProcedureName, parameters);
            if (typeof(T) == typeof(OdosDealerDetailsRow))
                return Task.FromResult((T?)(object?)_header);
            return Task.FromResult(default(T));
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            string configuredProcedureName,
            object? parameters = null,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            Record(configuredProcedureName, parameters);
            if (typeof(T) == typeof(OdosLegalDocumentRow))
                return Task.FromResult(_documents.Cast<T>().ToList() as IReadOnlyList<T> ?? Array.Empty<T>());
            return Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());
        }

        public Task<int> ExecuteAsync(
            string configuredProcedureName,
            object? parameters = null,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            Record(configuredProcedureName, parameters);
            return Task.FromResult(0);
        }

        public Task<T?> ExecuteScalarAsync<T>(
            string configuredProcedureName,
            object? parameters = null,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            Record(configuredProcedureName, parameters);
            return Task.FromResult(default(T));
        }

        public Task<Dapper.DynamicParameters> ExecuteWithOutputAsync(
            string configuredProcedureName,
            Dapper.DynamicParameters parameters,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            Record(configuredProcedureName, parameters);
            return Task.FromResult(parameters);
        }

        public Task<(TFirst?, IReadOnlyList<TSecond>)> QuerySingleAndListAsync<TFirst, TSecond>(
            string configuredProcedureName,
            object? parameters = null,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(IReadOnlyList<TFirst>, IReadOnlyList<TSecond>)> QueryListAndListAsync<TFirst, TSecond>(
            string configuredProcedureName,
            object? parameters = null,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private void Record(string procedureName, object? parameters)
        {
            ExecutedProcedures.Add(procedureName);
            ExecutedParameters.Add(parameters);
        }
    }
}

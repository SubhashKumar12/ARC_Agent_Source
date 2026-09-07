using Dapper;
using ARC.Data.Exceptions;
using ARC.Data.Odos;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.Entities;
using ARC.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ARC.Data.Sql;

public sealed class LegalCaseRepository : ILegalCaseRepository
{
    private const string Section138DocumentCategory = "04";

    private readonly IStoredProcedureExecutor _procedures;
    private readonly ISqlConnectionFactory? _connections;
    private readonly ILegalCaseCompletenessOverlayReader _overlay;
    private readonly IOdosDealerKeyResolver _odosKeys;
    private readonly OdosSqlSessionOptions _session;
    private readonly SqlStoredProcedureNames _spNames;
    private readonly ILogger<LegalCaseRepository> _logger;

    public LegalCaseRepository(
        IStoredProcedureExecutor procedures,
        ISqlConnectionFactory connections,
        IOdosDealerKeyResolver odosKeys,
        IOptions<OdosSqlSessionOptions> session,
        IOptions<SqlStoredProcedureNames> spNames,
        ILogger<LegalCaseRepository> logger)
        : this(
            procedures,
            connections,
            new SqlLegalCaseCompletenessOverlayReader(connections),
            odosKeys,
            session,
            spNames,
            logger)
    {
    }

    internal LegalCaseRepository(
        IStoredProcedureExecutor procedures,
        ISqlConnectionFactory? connections,
        ILegalCaseCompletenessOverlayReader overlay,
        IOdosDealerKeyResolver odosKeys,
        IOptions<OdosSqlSessionOptions> session,
        IOptions<SqlStoredProcedureNames> spNames,
        ILogger<LegalCaseRepository> logger)
    {
        _procedures = procedures;
        _connections = connections;
        _overlay = overlay;
        _odosKeys = odosKeys;
        _session = session.Value;
        _spNames = spNames.Value;
        _logger = logger;
    }

    public async Task<LegalCase?> GetAsync(DealerUrn urn, CancellationToken cancellationToken)
    {
        if (!_session.IsConfigured)
            throw new OdosSessionNotConfiguredException();

        var key = _odosKeys.Resolve(urn);

        try
        {
            var header = await _procedures.QuerySingleOrDefaultAsync<OdosDealerDetailsRow>(
                _spNames.GetLegalCase,
                new
                {
                    commt_year = _session.CommtYear,
                    commt_month = _session.CommtMonth,
                    depot_code = key.DepotCode,
                    dealer_code = key.DealerCode,
                    usp_user_id = _session.UserId
                },
                cancellationToken: cancellationToken);

            if (header?.legal_id is not { } legalId)
                return null;

            var documents = await _procedures.QueryAsync<OdosLegalDocumentRow>(
                _spNames.GetLegalDocuments,
                new
                {
                    ldoc_legal_id = legalId,
                    ldoc_category_code = Section138DocumentCategory,
                    ldoc_notice_id = (int?)null,
                    ldoc_id = (int?)null,
                    current_only = "Y"
                },
                cancellationToken: cancellationToken);

            var overlay = await _overlay.LoadAsync(urn, cancellationToken);

            _logger.LogInformation(
                "Legal case read for dealer URN {DealerUrn} via {ProcedureName}. LegalId={LegalId} S138DocCount={S138DocCount}",
                urn.Value,
                _spNames.GetLegalCase,
                legalId,
                documents.Count);

            var caseReference = legalId.ToString();
            var score = overlay?.CompletenessScore ?? 0m;
            var gaps = overlay?.Gaps ?? [];

            return new LegalCase(urn, score, gaps, caseReference);
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to load legal case.", ex);
        }
    }

    /// <summary>
    /// UpsertAsync persists ARC case-file completeness to dbo.LegalCase.
    /// ODOS status/document write SPs do not support this contract — see Phase 12C blocker doc.
    /// </summary>
    public async Task UpsertAsync(LegalCase legalCase, CancellationToken cancellationToken)
    {
        if (_connections is null)
            throw new InvalidOperationException("Upsert requires ISqlConnectionFactory.");

        const string sql = """
            MERGE dbo.LegalCase AS t
            USING (SELECT @DealerUrn AS DealerUrn) AS s
            ON t.DealerUrn = s.DealerUrn
            WHEN MATCHED THEN UPDATE SET
                CaseReference = @CaseReference,
                CompletenessScore = @CompletenessScore,
                GapsJson = @GapsJson
            WHEN NOT MATCHED THEN INSERT (DealerUrn, CaseReference, CompletenessScore, GapsJson)
                VALUES (@DealerUrn, @CaseReference, @CompletenessScore, @GapsJson);
            """;
        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                DealerUrn = legalCase.DealerUrn.Value,
                legalCase.CaseReference,
                legalCase.CompletenessScore,
                GapsJson = string.Join('\n', legalCase.Gaps)
            }, cancellationToken: cancellationToken));
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to persist legal case.", ex);
        }
    }
}

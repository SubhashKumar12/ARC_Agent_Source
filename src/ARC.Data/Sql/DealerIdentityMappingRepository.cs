using Microsoft.Data.SqlClient;
using ARC.Data.Exceptions;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.Enums;
using ARC.Domain.Identity;
using ARC.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ARC.Data.Sql;

/// <summary>SQL authority for SP2 source-identifier and alias binding. Does not mint dealers.</summary>
public sealed class DealerIdentityMappingRepository : IDealerIdentityMappingRepository
{
    private const int ConflictNumber = 50001;
    private readonly IStoredProcedureExecutor _procedures;
    private readonly SqlStoredProcedureNames _spNames;

    public DealerIdentityMappingRepository(
        IStoredProcedureExecutor procedures,
        IOptions<SqlStoredProcedureNames> spNames)
    {
        _procedures = procedures;
        _spNames = spNames.Value;
    }

    public async Task<DealerSourceMapping?> GetMappingAsync(
        string sourceSystem, string sourceIdentifier, CancellationToken cancellationToken)
    {
        var row = await _procedures.QuerySingleOrDefaultAsync<MappingRow>(
            _spNames.GetDealerSourceMapping,
            new
            {
                SourceSystem = DealerSourceSystems.Normalize(sourceSystem),
                SourceIdentifier = DealerIdentityNormalization.Identifier(sourceIdentifier)
            },
            cancellationToken: cancellationToken);
        return row?.ToDomain();
    }

    public async Task SaveResolvedMappingAsync(DealerSourceMapping mapping, CancellationToken cancellationToken)
    {
        try
        {
            await _procedures.ExecuteAsync(
                _spNames.SaveResolvedMapping,
                new
                {
                    mapping.SourceSystem,
                    mapping.SourceIdentifier,
                    CanonicalUrn = mapping.CanonicalUrn.Value,
                    MatchKind = mapping.MatchKind.ToString()
                },
                cancellationToken: cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is ConflictNumber or 2627 or 2601)
        {
            throw new DuplicatePersistenceException(
                $"Source identifier '{mapping.SourceSystem}/{mapping.SourceIdentifier}' cannot map to two canonical URNs.");
        }
        catch (DataAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DataAccessException("Failed to persist dealer source identifier mapping.", ex);
        }
    }

    public async Task<IReadOnlyList<DealerUrn>> FindDealerUrnsByIdentifierAsync(
        string sourceSystem, string sourceIdentifier, CancellationToken cancellationToken)
    {
        var rows = await _procedures.QueryAsync<string>(
            _spNames.FindDealerUrnsByIdentifier,
            new
            {
                SourceSystem = DealerSourceSystems.Normalize(sourceSystem),
                SourceIdentifier = DealerIdentityNormalization.Identifier(sourceIdentifier)
            },
            cancellationToken: cancellationToken);
        return rows.Select(u => new DealerUrn(u)).ToList();
    }

    public async Task<IReadOnlyList<DealerUrn>> FindDealerUrnsByAliasValueAsync(
        string normalizedAlias, CancellationToken cancellationToken)
    {
        var rows = await _procedures.QueryAsync<string>(
            _spNames.FindDealerUrnsByAlias,
            new { AliasValue = DealerIdentityNormalization.Alias(normalizedAlias) },
            cancellationToken: cancellationToken);
        return rows.Select(u => new DealerUrn(u)).ToList();
    }

    public async Task SaveAliasAsync(
        DealerUrn canonicalUrn, string aliasKind, string normalizedAlias, CancellationToken cancellationToken)
    {
        try
        {
            await _procedures.ExecuteAsync(
                _spNames.SaveDealerAlias,
                new
                {
                    CanonicalUrn = canonicalUrn.Value,
                    AliasKind = aliasKind,
                    AliasValue = DealerIdentityNormalization.Alias(normalizedAlias)
                },
                cancellationToken: cancellationToken);
        }
        catch (DataAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DataAccessException("Failed to persist dealer alias.", ex);
        }
    }

    private sealed class MappingRow
    {
        public string SourceSystem { get; set; } = "";
        public string SourceIdentifier { get; set; } = "";
        public string CanonicalUrn { get; set; } = "";
        public string MatchKind { get; set; } = "";

        public DealerSourceMapping ToDomain() => new(
            SourceSystem,
            SourceIdentifier,
            new DealerUrn(CanonicalUrn),
            Enum.Parse<DealerMatchKind>(MatchKind, ignoreCase: true));
    }
}

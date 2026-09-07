using Microsoft.Extensions.Logging;
using ARC.Data.Exceptions;
using ARC.Data.Sql;
using ARC.Domain.Enums;
using ARC.Domain.Identity;
using ARC.Domain.ValueObjects;
using ARC.Tools.Exceptions;

namespace ARC.Tools.Identity;

/// <summary>
/// Deterministic SP2 binder. No IChatClient, no embeddings, no mint of unknown dealers.
/// </summary>
public sealed class ResolveDealerIdentityTool
{
    public const string Name = "ResolveDealerIdentity";

    private readonly IDealerIdentityMappingRepository _mappings;
    private readonly ILogger<ResolveDealerIdentityTool> _logger;

    public ResolveDealerIdentityTool(
        IDealerIdentityMappingRepository mappings,
        ILogger<ResolveDealerIdentityTool> logger)
    {
        _mappings = mappings;
        _logger = logger;
    }

    public async Task<DealerIdentityResolution> ResolveAsync(
        SourceDealerRecord record, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _mappings.GetMappingAsync(record.SourceSystem, record.SourceIdentifier, cancellationToken);
            if (existing is not null)
            {
                return DealerIdentityResolution.Resolved(
                    existing.CanonicalUrn,
                    DealerMatchKind.ExistingMapping,
                    $"Durable mapping {record.SourceSystem}/{record.SourceIdentifier} → {existing.CanonicalUrn.Value}.");
            }

            var identifierMatches = await _mappings.FindDealerUrnsByIdentifierAsync(
                record.SourceSystem, record.SourceIdentifier, cancellationToken);
            if (identifierMatches.Count > 1)
            {
                return DealerIdentityResolution.Ambiguous(
                    $"Identifier {record.SourceSystem}/{record.SourceIdentifier} matches {identifierMatches.Count} dealers.");
            }

            if (identifierMatches.Count == 1)
            {
                return await PersistResolvedAsync(
                    record, identifierMatches[0], DealerMatchKind.ExactIdentifier, cancellationToken);
            }

            var aliasUrns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var alias in record.ProvidedAliases())
            {
                var found = await _mappings.FindDealerUrnsByAliasValueAsync(alias, cancellationToken);
                foreach (var urn in found)
                    aliasUrns.Add(urn.Value);
            }

            if (aliasUrns.Count > 1)
            {
                return DealerIdentityResolution.Ambiguous(
                    $"Supported aliases for {record.SourceSystem}/{record.SourceIdentifier} match {aliasUrns.Count} dealers.");
            }

            if (aliasUrns.Count == 1)
            {
                return await PersistResolvedAsync(
                    record, new DealerUrn(aliasUrns.First()), DealerMatchKind.ExactAlias, cancellationToken);
            }

            return DealerIdentityResolution.Unresolved(
                $"No exact identifier or supported alias match for {record.SourceSystem}/{record.SourceIdentifier}. Canonical URN is not minted.");
        }
        catch (DuplicatePersistenceException ex)
        {
            return DealerIdentityResolution.Ambiguous(ex.Message);
        }
        catch (DataAccessException ex)
        {
            throw new ToolException(Name, "Failed to resolve dealer identity.", ex);
        }
    }

    private async Task<DealerIdentityResolution> PersistResolvedAsync(
        SourceDealerRecord record,
        DealerUrn canonicalUrn,
        DealerMatchKind matchKind,
        CancellationToken cancellationToken)
    {
        await _mappings.SaveResolvedMappingAsync(
            new DealerSourceMapping(record.SourceSystem, record.SourceIdentifier, canonicalUrn, matchKind),
            cancellationToken);

        _logger.LogInformation(
            "Tool {Tool} resolved {SourceSystem}/{SourceIdentifier} to {DealerUrn} via {MatchKind}",
            Name, record.SourceSystem, record.SourceIdentifier, canonicalUrn.Value, matchKind);

        return DealerIdentityResolution.Resolved(
            canonicalUrn,
            matchKind,
            $"{matchKind} bound {record.SourceSystem}/{record.SourceIdentifier} to {canonicalUrn.Value}.");
    }
}

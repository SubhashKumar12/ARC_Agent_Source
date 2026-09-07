using ARC.Data.Exceptions;
using ARC.Domain.Entities;
using ARC.Domain.Identity;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Sql;

/// <summary>Process-local SP2 map for Shadow CLI and unit tests. Production identity authority is SQL.</summary>
public sealed class InMemoryDealerIdentityMappingRepository : IDealerIdentityMappingRepository
{
    private readonly IDealerRepository _dealers;
    private readonly object _gate = new();
    private readonly Dictionary<(string System, string Identifier), DealerSourceMapping> _mappings = new();
    private readonly List<(string Urn, string Kind, string Value)> _aliases = [];

    public InMemoryDealerIdentityMappingRepository(IDealerRepository dealers) => _dealers = dealers;

    public void Reset()
    {
        lock (_gate)
        {
            _mappings.Clear();
            _aliases.Clear();
        }
    }

    public Task<DealerSourceMapping?> GetMappingAsync(
        string sourceSystem, string sourceIdentifier, CancellationToken cancellationToken)
    {
        var key = Key(sourceSystem, sourceIdentifier);
        lock (_gate)
            return Task.FromResult(_mappings.GetValueOrDefault(key));
    }

    public Task SaveResolvedMappingAsync(DealerSourceMapping mapping, CancellationToken cancellationToken)
    {
        var key = (mapping.SourceSystem, mapping.SourceIdentifier);
        lock (_gate)
        {
            if (_mappings.TryGetValue(key, out var existing)
                && !string.Equals(existing.CanonicalUrn.Value, mapping.CanonicalUrn.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new DuplicatePersistenceException(
                    $"Source identifier '{mapping.SourceSystem}/{mapping.SourceIdentifier}' cannot map to two canonical URNs.");
            }

            _mappings[key] = mapping;
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<DealerUrn>> FindDealerUrnsByIdentifierAsync(
        string sourceSystem, string sourceIdentifier, CancellationToken cancellationToken)
    {
        var system = DealerSourceSystems.Normalize(sourceSystem);
        var id = DealerIdentityNormalization.Identifier(sourceIdentifier);
        var dealers = await _dealers.ListAllAsync(cancellationToken);
        var matches = dealers
            .Where(d => IdentifierOf(d, system) is { } value
                        && !string.IsNullOrWhiteSpace(value)
                        && string.Equals(DealerIdentityNormalization.Identifier(value), id, StringComparison.Ordinal))
            .Select(d => d.Urn)
            .Distinct()
            .ToList();
        return matches;
    }

    public Task<IReadOnlyList<DealerUrn>> FindDealerUrnsByAliasValueAsync(
        string normalizedAlias, CancellationToken cancellationToken)
    {
        var alias = DealerIdentityNormalization.Alias(normalizedAlias);
        lock (_gate)
        {
            IReadOnlyList<DealerUrn> urns = _aliases
                .Where(a => string.Equals(a.Value, alias, StringComparison.Ordinal))
                .Select(a => a.Urn)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(u => new DealerUrn(u))
                .ToList();
            return Task.FromResult(urns);
        }
    }

    public Task SaveAliasAsync(
        DealerUrn canonicalUrn, string aliasKind, string normalizedAlias, CancellationToken cancellationToken)
    {
        var alias = DealerIdentityNormalization.Alias(normalizedAlias);
        lock (_gate)
        {
            if (!_aliases.Any(a =>
                    string.Equals(a.Urn, canonicalUrn.Value, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.Kind, aliasKind, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.Value, alias, StringComparison.Ordinal)))
            {
                _aliases.Add((canonicalUrn.Value, aliasKind, alias));
            }
        }

        return Task.CompletedTask;
    }

    private static (string System, string Identifier) Key(string sourceSystem, string sourceIdentifier)
        => (DealerSourceSystems.Normalize(sourceSystem), DealerIdentityNormalization.Identifier(sourceIdentifier));

    private static string? IdentifierOf(Dealer dealer, string sourceSystem)
        => sourceSystem switch
        {
            DealerSourceSystems.Sap => dealer.SapCode,
            DealerSourceSystems.Portal => dealer.PortalId,
            DealerSourceSystems.FieldApp => dealer.AppId,
            _ => null
        };
}

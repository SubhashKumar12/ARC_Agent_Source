using Microsoft.Extensions.Options;
using ARC.Data.Exceptions;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Odos;

/// <summary>
/// Resolves canonical DealerUrn to production ODOS depot/dealer keys via explicit configuration.
/// Does not infer keys from URN string structure or SAP/portal identifiers.
/// </summary>
public sealed class OdosDealerKeyResolver : IOdosDealerKeyResolver
{
    private readonly OdosDealerKeyOptions _options;

    public OdosDealerKeyResolver(IOptions<OdosDealerKeyOptions> options)
        => _options = options.Value;

    public OdosDealerKey Resolve(DealerUrn urn)
    {
        if (OdosChatDealerUrn.TryParse(urn, out var chatKey))
            return chatKey;

        if (!_options.ByUrn.TryGetValue(urn.Value, out var entry)
            || string.IsNullOrWhiteSpace(entry.DepotCode)
            || string.IsNullOrWhiteSpace(entry.DealerCode))
        {
            throw new DataAccessException(
                $"No ODOS depot/dealer mapping configured for dealer URN '{urn.Value}'.");
        }

        var key = new OdosDealerKey(entry.DepotCode.Trim(), entry.DealerCode.Trim());
        key.Validate();
        return key;
    }

    public OdosDealerKey ResolveExplicit(string depotCode, string dealerCode)
    {
        var key = new OdosDealerKey(depotCode.Trim(), dealerCode.Trim());
        key.Validate();
        return key;
    }
}

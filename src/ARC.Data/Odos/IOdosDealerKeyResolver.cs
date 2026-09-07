using ARC.Domain.ValueObjects;

namespace ARC.Data.Odos;

public interface IOdosDealerKeyResolver
{
    OdosDealerKey Resolve(DealerUrn urn);

    /// <summary>
    /// Validates explicitly supplied ODOS keys. Does not infer depot from dealer code alone.
    /// </summary>
    OdosDealerKey ResolveExplicit(string depotCode, string dealerCode);
}

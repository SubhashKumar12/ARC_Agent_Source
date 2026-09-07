namespace ARC.Data.Odos;

/// <summary>
/// Production ODOS dealer scope key ({depot_code}, {dealer_code}).
/// Resolved from configuration — not inferred from URN semantics.
/// </summary>
public sealed record OdosDealerKey(string DepotCode, string DealerCode)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DepotCode))
            throw new InvalidOperationException("ODOS depot_code is required.");
        if (string.IsNullOrWhiteSpace(DealerCode))
            throw new InvalidOperationException("ODOS dealer_code is required.");
    }
}

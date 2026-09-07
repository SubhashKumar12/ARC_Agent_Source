namespace ARC.Data.Odos;

/// <summary>
/// Explicit URN → ODOS depot/dealer mapping. No defaults — unmapped URNs fail closed.
/// </summary>
public sealed class OdosDealerKeyOptions
{
    public const string SectionName = "ArcData:Odos:DealerKeys";

    /// <summary>Canonical DealerUrn value → ODOS depot_code + dealer_code.</summary>
    public Dictionary<string, OdosDealerKeyEntry> ByUrn { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OdosDealerKeyEntry
{
    public string DepotCode { get; set; } = "";
    public string DealerCode { get; set; } = "";
}

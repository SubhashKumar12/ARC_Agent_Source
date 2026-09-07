using System.Text.RegularExpressions;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Odos;

/// <summary>
/// Session-scoped URN for explicit ODOS depot/dealer chat lookups. Not SP2 canonical identity.
/// </summary>
public static partial class OdosChatDealerUrn
{
    public const string Prefix = "odos-chat:";

    private const int MaxDepotCodeLength = 10;
    private const int MaxDealerCodeLength = 20;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-_.]{0,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex DepotCodePattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-_.]{0,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex DealerCodePattern();

    public static DealerUrn ForKeys(string depotCode, string dealerCode)
        => new($"{Prefix}{depotCode.Trim()}:{dealerCode.Trim()}");

    /// <summary>
    /// Parses an ARC-generated chat URN into validated ODOS keys. Returns false for any other URN shape.
    /// </summary>
    public static bool TryParse(DealerUrn urn, out OdosDealerKey key)
    {
        key = default!;
        if (string.IsNullOrWhiteSpace(urn.Value))
            return false;

        if (!urn.Value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var remainder = urn.Value[Prefix.Length..];
        var separator = remainder.IndexOf(':');
        if (separator <= 0 || separator >= remainder.Length - 1)
            return false;

        var depotCode = remainder[..separator].Trim();
        var dealerCode = remainder[(separator + 1)..].Trim();
        if (depotCode.Length > MaxDepotCodeLength || dealerCode.Length > MaxDealerCodeLength)
            return false;

        if (!DepotCodePattern().IsMatch(depotCode) || !DealerCodePattern().IsMatch(dealerCode))
            return false;

        key = new OdosDealerKey(depotCode, dealerCode);
        key.Validate();
        return true;
    }
}

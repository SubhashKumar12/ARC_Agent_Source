using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;

namespace ARC.Domain.Identity;

/// <summary>Known source systems for SP2 binding. Do not invent GST/mobile/PAN.</summary>
public static class DealerSourceSystems
{
    public const string Sap = "SAP";
    public const string Portal = "PORTAL";
    public const string FieldApp = "FIELD-APP";

    public static bool IsKnown(string sourceSystem)
        => string.Equals(sourceSystem, Sap, StringComparison.OrdinalIgnoreCase)
           || string.Equals(sourceSystem, Portal, StringComparison.OrdinalIgnoreCase)
           || string.Equals(sourceSystem, FieldApp, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string sourceSystem)
    {
        if (string.Equals(sourceSystem, Sap, StringComparison.OrdinalIgnoreCase))
            return Sap;
        if (string.Equals(sourceSystem, Portal, StringComparison.OrdinalIgnoreCase))
            return Portal;
        if (string.Equals(sourceSystem, FieldApp, StringComparison.OrdinalIgnoreCase))
            return FieldApp;
        throw new ArgumentException($"Unknown dealer source system '{sourceSystem}'.", nameof(sourceSystem));
    }
}

public static class DealerAliasKinds
{
    public const string TradeLegalName = "TradeLegalName";
    public const string ChequeAccountName = "ChequeAccountName";
    public const string AgreementParty = "AgreementParty";
}

/// <summary>Conservative ID/alias folding for exact match only. No fuzzy, no embeddings.</summary>
public static class DealerIdentityNormalization
{
    public static string Identifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Source identifier is required.", nameof(value));
        return value.Trim().ToUpperInvariant();
    }

    public static string Alias(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Alias is required.", nameof(value));
        var collapsed = string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return collapsed.ToUpperInvariant();
    }
}

/// <summary>Inbound source-system dealer record. Canonical URN is the resolver output, not an input.</summary>
public sealed record SourceDealerRecord
{
    public string SourceSystem { get; }
    public string SourceIdentifier { get; }
    public string? TradeLegalName { get; }
    public string? ChequeAccountName { get; }
    public string? AgreementParty { get; }

    public SourceDealerRecord(
        string sourceSystem,
        string sourceIdentifier,
        string? tradeLegalName = null,
        string? chequeAccountName = null,
        string? agreementParty = null)
    {
        SourceSystem = DealerSourceSystems.Normalize(sourceSystem);
        SourceIdentifier = DealerIdentityNormalization.Identifier(sourceIdentifier);
        TradeLegalName = EmptyToNull(tradeLegalName);
        ChequeAccountName = EmptyToNull(chequeAccountName);
        AgreementParty = EmptyToNull(agreementParty);
    }

    public IReadOnlyList<string> ProvidedAliases()
    {
        var aliases = new List<string>();
        Add(aliases, TradeLegalName);
        Add(aliases, ChequeAccountName);
        Add(aliases, AgreementParty);
        return aliases;
    }

    private static void Add(List<string> aliases, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;
        aliases.Add(DealerIdentityNormalization.Alias(raw));
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record DealerIdentityResolution(
    DealerIdentityStatus Status,
    DealerUrn? CanonicalUrn,
    DealerMatchKind? MatchKind,
    string Reason)
{
    public static DealerIdentityResolution Resolved(DealerUrn urn, DealerMatchKind matchKind, string reason)
        => new(DealerIdentityStatus.Resolved, urn, matchKind, reason);

    public static DealerIdentityResolution Unresolved(string reason)
        => new(DealerIdentityStatus.Unresolved, null, null, reason);

    public static DealerIdentityResolution Ambiguous(string reason)
        => new(DealerIdentityStatus.Ambiguous, null, null, reason);
}

/// <summary>Unresolved/ambiguous identity must not proceed to exposure or G1.</summary>
public static class DealerIdentityProgression
{
    public static bool AllowsExposure(DealerIdentityResolution resolution)
        => resolution.Status == DealerIdentityStatus.Resolved && resolution.CanonicalUrn is { } urn && !string.IsNullOrWhiteSpace(urn.Value);
}

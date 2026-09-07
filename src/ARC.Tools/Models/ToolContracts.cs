namespace ARC.Tools.Models;

public sealed class ArcToolsOptions
{
    public const string SectionName = "ArcTools";

    /// <summary>Process B trigger from source: demand notice + no payment in 60 days.</summary>
    public int Section138NonPaymentDays { get; set; } = 60;

    /// <summary>A2 visit-tier cutoff. Not in source — leave null so Visit is never auto-assigned.</summary>
    public decimal? VisitMaxNetExposure { get; set; }

    /// <summary>ASR below this requires TSI confirmation. Hard floor value is To Be Confirmed.</summary>
    public decimal? VoicePtpConfirmBelow { get; set; }

    /// <summary>ASR below this discards the capture. Hard floor value is To Be Confirmed. Null = do not discard.</summary>
    public decimal? VoicePtpDiscardBelow { get; set; }

    /// <summary>Azure Speech endpoint. Empty uses the demo transcriber. No keys — Managed Identity.</summary>
    public string SpeechEndpoint { get; set; } = "";

    public bool SpeechUseManagedIdentity { get; set; } = true;

    /// <summary>Allow-list of BCP-47 locales. Do not derive from dealer Region.</summary>
    public string[] SpeechAllowedLocales { get; set; } = ["en-IN"];

    public FieldPersistenceOptions FieldPersistence { get; set; } = new();

    public int SpeechTimeoutSeconds { get; set; } = 30;

    public bool IsSpeechLocaleAllowed(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
            return false;
        var allowed = SpeechAllowedLocales is { Length: > 0 } ? SpeechAllowedLocales : ["en-IN"];
        return allowed.Any(a => string.Equals(a, locale.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public string ConfidenceCategory(decimal? speechConfidence)
    {
        if (speechConfidence is null)
            return "unknown";
        if (VoicePtpDiscardBelow is { } floor && speechConfidence.Value < floor)
            return "discard";
        if (VoicePtpConfirmBelow is { } confirm && speechConfidence.Value < confirm)
            return "confirm";
        return "ok";
    }
}

public sealed record ToolCallContext(string? CycleId, string? CorrelationId, DateOnly AsOf);

public sealed class FieldPersistenceOptions
{
    /// <summary>Explicit opt-in for tests/CLI. Production hosts without ArcData:Sql must not use in-memory field stores.</summary>
    public bool UseInMemory { get; set; }
}

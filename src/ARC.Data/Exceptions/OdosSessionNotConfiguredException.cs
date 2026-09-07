namespace ARC.Data.Exceptions;

/// <summary>
/// ODOS period/session scope (CommtYear, CommtMonth, UserId) is not configured.
/// Callers must surface structured unavailability — not HTTP 409 for multi-capability chat.
/// </summary>
public sealed class OdosSessionNotConfiguredException : Exception
{
    public const string ReasonCode = "ODOS_SESSION_NOT_CONFIGURED";

    public OdosSessionNotConfiguredException()
        : base("ArcData:Odos:Session requires CommtYear, CommtMonth, and UserId for ODOS dealer-detail reads.")
    {
    }
}

namespace ARC.Api.Chat;

/// <summary>Internal reason codes for metric unavailability. Mapped to user-friendly text in the composer.</summary>
public static class BusinessChatUnavailabilityCodes
{
    public const string OdosSessionNotConfigured = "ODOS_SESSION_NOT_CONFIGURED";
    public const string FieldPersistenceNotConfigured = "FIELD_PERSISTENCE_NOT_CONFIGURED";
}

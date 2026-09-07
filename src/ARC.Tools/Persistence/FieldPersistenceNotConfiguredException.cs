namespace ARC.Tools.Persistence;

/// <summary>
/// ARC-owned field persistence (PTP/visit/chase) has no configured SQL store.
/// </summary>
public sealed class FieldPersistenceNotConfiguredException : Exception
{
    public const string ReasonCode = "FIELD_PERSISTENCE_NOT_CONFIGURED";

    public FieldPersistenceNotConfiguredException()
        : base("ARC field persistence SQL is not configured.")
    {
    }
}

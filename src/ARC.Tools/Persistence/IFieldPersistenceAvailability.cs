namespace ARC.Tools.Persistence;

/// <summary>Reports whether ARC-owned PTP/visit/chase persistence is SQL-backed, in-memory (tests), or absent.</summary>
public interface IFieldPersistenceAvailability
{
    bool IsSqlConfigured { get; }

    bool IsInMemory { get; }

    bool IsProductionNotConfigured { get; }
}

internal sealed class FieldPersistenceAvailability : IFieldPersistenceAvailability
{
    public FieldPersistenceAvailability(bool isSqlConfigured, bool isInMemory)
    {
        IsSqlConfigured = isSqlConfigured;
        IsInMemory = isInMemory;
        IsProductionNotConfigured = !isSqlConfigured && !isInMemory;
    }

    public bool IsSqlConfigured { get; }

    public bool IsInMemory { get; }

    public bool IsProductionNotConfigured { get; }
}

namespace ARC.Domain.ValueObjects;

/// <summary>R6 lineage: every quoted amount must resolve to source rows.</summary>
public sealed record LineItemRef
{
    public string SourceSystem { get; }
    public string SourceTable { get; }
    public string SourceKey { get; }
    public decimal Amount { get; }
    public DateOnly PostedOn { get; }

    public LineItemRef(
        string sourceSystem,
        string sourceTable,
        string sourceKey,
        decimal amount,
        DateOnly postedOn)
    {
        if (string.IsNullOrWhiteSpace(sourceSystem))
            throw new ArgumentException("SourceSystem is required.", nameof(sourceSystem));
        if (string.IsNullOrWhiteSpace(sourceTable))
            throw new ArgumentException("SourceTable is required.", nameof(sourceTable));
        if (string.IsNullOrWhiteSpace(sourceKey))
            throw new ArgumentException("SourceKey is required.", nameof(sourceKey));

        SourceSystem = sourceSystem;
        SourceTable = sourceTable;
        SourceKey = sourceKey;
        Amount = amount;
        PostedOn = postedOn;
    }
}

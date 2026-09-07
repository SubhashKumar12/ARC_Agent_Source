namespace ARC.Domain.ValueObjects;

/// <summary>Monthly ODOS / S138 cycle identifier. Idempotency key component: (cycle_id, dealer_urn, node).</summary>
public readonly record struct CycleId
{
    public string Value { get; }

    public CycleId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Cycle ID cannot be empty.", nameof(value));
        Value = value.Trim();
    }

    public override string ToString() => Value;
}

namespace ARC.Domain.ValueObjects;

public readonly record struct CorrelationId
{
    public string Value { get; }

    public CorrelationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Correlation ID cannot be empty.", nameof(value));
        Value = value.Trim();
    }

    public static CorrelationId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}

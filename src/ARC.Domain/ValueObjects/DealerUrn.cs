namespace ARC.Domain.ValueObjects;

/// <summary>Resolved dealer identity across SAP, portal, app, cheque and agreement (SP2).</summary>
public readonly record struct DealerUrn
{
    public string Value { get; }

    public DealerUrn(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Dealer URN cannot be empty.", nameof(value));
        Value = value.Trim();
    }

    public override string ToString() => Value;

    public static implicit operator string(DealerUrn urn) => urn.Value;
}

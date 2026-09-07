namespace ARC.Domain.ValueObjects;

/// <summary>INR amounts as decimal. Never use floating point for money.</summary>
public readonly record struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency = "INR")
    {
        Currency = string.IsNullOrWhiteSpace(currency) ? "INR" : currency.Trim().ToUpperInvariant();
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    public static Money Zero => new(0m);

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount + b.Amount, a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount - b.Amount, a.Currency);
    }

    public bool IsNegative => Amount < 0m;

    public override string ToString() => $"{Currency} {Amount:N2}";

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (!string.Equals(a.Currency, b.Currency, StringComparison.Ordinal))
            throw new InvalidOperationException($"Cannot combine {a.Currency} and {b.Currency}.");
    }
}

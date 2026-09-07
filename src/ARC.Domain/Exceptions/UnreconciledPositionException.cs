namespace ARC.Domain.Exceptions;

public sealed class UnreconciledPositionException : DomainException
{
    public UnreconciledPositionException(string dealerUrn)
        : base($"Dealer '{dealerUrn}' is UNRECONCILED. No notice or legal action may proceed.")
    {
        DealerUrn = dealerUrn;
    }

    public string DealerUrn { get; }
}

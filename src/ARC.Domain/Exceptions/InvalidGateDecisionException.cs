namespace ARC.Domain.Exceptions;

public sealed class InvalidGateDecisionException : DomainException
{
    public InvalidGateDecisionException(string message) : base(message)
    {
    }
}

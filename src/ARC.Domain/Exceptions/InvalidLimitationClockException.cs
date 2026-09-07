namespace ARC.Domain.Exceptions;

public sealed class InvalidLimitationClockException : DomainException
{
    public InvalidLimitationClockException(string message) : base(message)
    {
    }
}

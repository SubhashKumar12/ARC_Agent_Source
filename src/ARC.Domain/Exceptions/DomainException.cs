namespace ARC.Domain.Exceptions;

/// <summary>Base type for recoverable domain-rule failures.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message)
    {
    }
}

namespace ARC.Data.Exceptions;

public class DataAccessException : Exception
{
    public DataAccessException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public sealed class EntityNotFoundException : DataAccessException
{
    public EntityNotFoundException(string entity, string key)
        : base($"{entity} '{key}' was not found.")
    {
        Entity = entity;
        Key = key;
    }

    public string Entity { get; }
    public string Key { get; }
}

public sealed class DuplicatePersistenceException : DataAccessException
{
    public DuplicatePersistenceException(string message) : base(message)
    {
    }
}

public sealed class StorageAccessException : DataAccessException
{
    public StorageAccessException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public sealed class MessagingAccessException : DataAccessException
{
    public MessagingAccessException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

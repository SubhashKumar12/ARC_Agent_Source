using Microsoft.Extensions.Options;

namespace ARC.Data.Sql.StoredProcedures;

/// <summary>
/// Fake stored procedure executor for tests/CLI.
/// Returns empty results; does not execute real SQL or connect to a database.
/// Enforces allow-list like the real executor.
/// </summary>
public sealed class FakeStoredProcedureExecutor
{
    private readonly SqlStoredProcedureNames _names;
    private readonly HashSet<string> _allowList;

    public FakeStoredProcedureExecutor(IOptions<SqlStoredProcedureNames> names)
    {
        _names = names.Value;
        _allowList = BuildAllowList();
    }

    public Task<T?> QuerySingleOrDefaultAsync<T>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed(configuredProcedureName);
        return Task.FromResult(default(T));
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed(configuredProcedureName);
        return Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());
    }

    public Task<int> ExecuteAsync(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed(configuredProcedureName);
        return Task.FromResult(0);
    }

    public Task<T?> ExecuteScalarAsync<T>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed(configuredProcedureName);
        return Task.FromResult(default(T));
    }

    private void EnsureAllowed(string procedureName)
    {
        if (!_allowList.Contains(procedureName))
            throw new UnauthorizedAccessException(
                $"Stored procedure '{procedureName}' is not allow-listed for execution.");
    }

    private HashSet<string> BuildAllowList()
    {
        var props = typeof(SqlStoredProcedureNames).GetProperties();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in props)
        {
            if (prop.PropertyType == typeof(string) && prop.GetValue(_names) is string value)
                set.Add(value);
        }
        return set;
    }
}

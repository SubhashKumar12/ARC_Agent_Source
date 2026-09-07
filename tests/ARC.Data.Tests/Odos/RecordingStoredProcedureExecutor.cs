using Dapper;
using ARC.Data.Sql.StoredProcedures;

namespace ARC.Data.Tests.Odos;

/// <summary>Test double that records the resolved procedure name and returns canned rows.</summary>
internal sealed class RecordingStoredProcedureExecutor : IStoredProcedureExecutor
{
    private readonly Dictionary<Type, object> _results = new();

    public List<string> ExecutedProcedures { get; } = [];
    public List<object?> ExecutedParameters { get; } = [];

    public void SetResult<T>(IReadOnlyList<T> rows) => _results[typeof(T)] = rows;
    public void SetSingle<T>(T row) where T : class => _results[typeof(T)] = new List<T> { row };

    public Task<IReadOnlyList<T>> QueryAsync<T>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        Record(configuredProcedureName, parameters);
        return Task.FromResult(Rows<T>());
    }

    public Task<T?> QuerySingleOrDefaultAsync<T>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        Record(configuredProcedureName, parameters);
        return Task.FromResult(Rows<T>().FirstOrDefault());
    }

    public Task<int> ExecuteAsync(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        Record(configuredProcedureName, parameters);
        return Task.FromResult(0);
    }

    public Task<T?> ExecuteScalarAsync<T>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        Record(configuredProcedureName, parameters);
        return Task.FromResult(default(T));
    }

    public Task<DynamicParameters> ExecuteWithOutputAsync(
        string configuredProcedureName,
        DynamicParameters parameters,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        Record(configuredProcedureName, parameters);
        return Task.FromResult(parameters);
    }

    public Task<(TFirst?, IReadOnlyList<TSecond>)> QuerySingleAndListAsync<TFirst, TSecond>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        Record(configuredProcedureName, parameters);
        var first = Rows<TFirst>().FirstOrDefault();
        var second = Rows<TSecond>();
        return Task.FromResult<(TFirst?, IReadOnlyList<TSecond>)>((first, second));
    }

    public Task<(IReadOnlyList<TFirst>, IReadOnlyList<TSecond>)> QueryListAndListAsync<TFirst, TSecond>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        Record(configuredProcedureName, parameters);
        return Task.FromResult<(IReadOnlyList<TFirst>, IReadOnlyList<TSecond>)>((Rows<TFirst>(), Rows<TSecond>()));
    }

    private void Record(string procedureName, object? parameters)
    {
        ExecutedProcedures.Add(procedureName);
        ExecutedParameters.Add(parameters);
    }

    private IReadOnlyList<T> Rows<T>()
        => _results.TryGetValue(typeof(T), out var rows) && rows is IReadOnlyList<T> typed
            ? typed
            : Array.Empty<T>();
}

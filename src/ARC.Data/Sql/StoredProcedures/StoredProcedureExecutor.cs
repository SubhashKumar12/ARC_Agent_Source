using System.Data;
using System.Diagnostics;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ARC.Data.Sql.StoredProcedures;

/// <summary>
/// Internal stored procedure executor. Supports typed query/execute operations.
/// NOT exposed to Agents/Tools/API — repositories use this internally only.
/// Enforces allow-list, telemetry, timeout, cancellation, no sensitive param logging.
/// </summary>
internal sealed class StoredProcedureExecutor : IStoredProcedureExecutor
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly SqlStoredProcedureNames _names;
    private readonly SqlStoredProcedureOptions _options;
    private readonly ILogger<StoredProcedureExecutor> _logger;
    private readonly HashSet<string> _allowList;

    public StoredProcedureExecutor(
        ISqlConnectionFactory connectionFactory,
        IOptions<SqlStoredProcedureNames> names,
        IOptions<SqlStoredProcedureOptions> options,
        ILogger<StoredProcedureExecutor> logger)
    {
        _connectionFactory = connectionFactory;
        _names = names.Value;
        _options = options.Value;
        _logger = logger;
        _allowList = BuildAllowList();
    }

    /// <summary>Query single row or default (SELECT single record).</summary>
    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed(configuredProcedureName);
        using var activity = StartActivity(configuredProcedureName, correlationId);
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var command = CreateCommand(configuredProcedureName, parameters, cancellationToken);

        try
        {
            var result = await connection.QuerySingleOrDefaultAsync<T>(command);
            activity?.SetTag("result.exists", result is not null);
            return result;
        }
        catch (Exception ex)
        {
            LogError(configuredProcedureName, ex, correlationId);
            throw;
        }
    }

    /// <summary>Query multiple rows (SELECT list).</summary>
    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed(configuredProcedureName);
        using var activity = StartActivity(configuredProcedureName, correlationId);
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var command = CreateCommand(configuredProcedureName, parameters, cancellationToken);

        try
        {
            var rows = await connection.QueryAsync<T>(command);
            var list = rows.ToList();
            activity?.SetTag("result.count", list.Count);
            return list;
        }
        catch (Exception ex)
        {
            LogError(configuredProcedureName, ex, correlationId);
            throw;
        }
    }

    /// <summary>Execute non-query (INSERT/UPDATE/DELETE/MERGE) — returns rows affected.</summary>
    public async Task<int> ExecuteAsync(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed(configuredProcedureName);
        using var activity = StartActivity(configuredProcedureName, correlationId);
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var command = CreateCommand(configuredProcedureName, parameters, cancellationToken);

        try
        {
            var affected = await connection.ExecuteAsync(command);
            activity?.SetTag("result.rows_affected", affected);
            return affected;
        }
        catch (Exception ex)
        {
            LogError(configuredProcedureName, ex, correlationId);
            throw;
        }
    }

    /// <summary>Execute scalar (SELECT COUNT, SELECT single value).</summary>
    public async Task<T?> ExecuteScalarAsync<T>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed(configuredProcedureName);
        using var activity = StartActivity(configuredProcedureName, correlationId);
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var command = CreateCommand(configuredProcedureName, parameters, cancellationToken);

        try
        {
            var result = await connection.ExecuteScalarAsync<T>(command);
            activity?.SetTag("result.value", result?.ToString() ?? "(null)");
            return result;
        }
        catch (Exception ex)
        {
            LogError(configuredProcedureName, ex, correlationId);
            throw;
        }
    }

    /// <summary>Execute with output parameters (ADOS call + out params).</summary>
    public async Task<DynamicParameters> ExecuteWithOutputAsync(
        string configuredProcedureName,
        DynamicParameters parameters,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed(configuredProcedureName);
        using var activity = StartActivity(configuredProcedureName, correlationId);
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            configuredProcedureName,
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: _options.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        try
        {
            await connection.ExecuteAsync(command);
            activity?.SetTag("result.has_output", true);
            return parameters;
        }
        catch (Exception ex)
        {
            LogError(configuredProcedureName, ex, correlationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<(TFirst?, IReadOnlyList<TSecond>)> QuerySingleAndListAsync<TFirst, TSecond>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed(configuredProcedureName);
        using var activity = StartActivity(configuredProcedureName, correlationId);
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var command = CreateCommand(configuredProcedureName, parameters, cancellationToken);

        try
        {
            await using var multi = await connection.QueryMultipleAsync(command);
            var first = await multi.ReadSingleOrDefaultAsync<TFirst>();
            var second = (await multi.ReadAsync<TSecond>()).ToList();
            activity?.SetTag("result.exists", first is not null);
            activity?.SetTag("result.count", second.Count);
            return (first, second);
        }
        catch (Exception ex)
        {
            LogError(configuredProcedureName, ex, correlationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<TFirst>, IReadOnlyList<TSecond>)> QueryListAndListAsync<TFirst, TSecond>(
        string configuredProcedureName,
        object? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed(configuredProcedureName);
        using var activity = StartActivity(configuredProcedureName, correlationId);
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var command = CreateCommand(configuredProcedureName, parameters, cancellationToken);

        try
        {
            await using var multi = await connection.QueryMultipleAsync(command);
            var first = (await multi.ReadAsync<TFirst>()).ToList();
            var second = (await multi.ReadAsync<TSecond>()).ToList();
            activity?.SetTag("result.count", first.Count);
            activity?.SetTag("result.secondary_count", second.Count);
            return (first, second);
        }
        catch (Exception ex)
        {
            LogError(configuredProcedureName, ex, correlationId);
            throw;
        }
    }

    private CommandDefinition CreateCommand(
        string configuredProcedureName,
        object? parameters,
        CancellationToken cancellationToken)
        => new(
            configuredProcedureName,
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: _options.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

    private void EnsureAllowed(string procedureName)
    {
        if (!_allowList.Contains(procedureName))
        {
            _logger.LogError(
                "Stored procedure {ProcedureName} is not in the allow-list. Execution blocked.",
                procedureName);
            throw new UnauthorizedAccessException(
                $"Stored procedure '{procedureName}' is not allow-listed for execution.");
        }
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

    private Activity? StartActivity(string procedureName, string? correlationId)
    {
        var activity = Activity.Current?.Source.StartActivity(
            $"StoredProcedure: {procedureName}",
            ActivityKind.Client);
        activity?.SetTag("db.system", "mssql");
        activity?.SetTag("db.operation", procedureName);
        activity?.SetTag("db.sql.stored_procedure", procedureName);
        if (!string.IsNullOrWhiteSpace(correlationId))
            activity?.SetTag("correlation.id", correlationId);
        return activity;
    }

    private void LogError(string procedureName, Exception ex, string? correlationId)
    {
        _logger.LogError(
            ex,
            "Stored procedure {ProcedureName} failed. CorrelationId: {CorrelationId}",
            procedureName,
            correlationId ?? "(none)");
    }
}

public sealed class SqlStoredProcedureOptions
{
    public const string SectionName = "ArcData:Sql:StoredProcedureOptions";

    /// <summary>Default command timeout in seconds. Production default 30s; high-volume queries may need 60s+.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Enable telemetry activity creation. Default true.</summary>
    public bool EnableTelemetry { get; set; } = true;
}

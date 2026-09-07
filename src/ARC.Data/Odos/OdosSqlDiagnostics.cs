using Microsoft.Extensions.Options;
using ARC.Data.Configuration;
using ARC.Data.Sql;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.Odos;

namespace ARC.Data.Odos;

public sealed record OdosSqlProbe(string Capability, string ProcedureName, bool Available, string? Detail);

public sealed record OdosSqlDiagnosticsResult(
    bool Configured,
    string ConfiguredTarget,
    bool ConnectionOpened,
    string DatabaseName,
    bool DatabaseApproved,
    string? ConnectionError,
    IReadOnlyList<OdosSqlProbe> Probes)
{
    public bool Passed => ConnectionOpened && DatabaseApproved && Probes.All(p => p.Available);
}

/// <summary>
/// Read-only connectivity diagnostic. Confirms a connection opens, the target database is the
/// approved one, and both approved stored procedures are reachable.
///
/// Procedure reachability is proven by executing the approved procedures with safe sample
/// parameters — no inline metadata SQL against sys.procedures, so the
/// stored-procedure-only architecture holds. Never prints credentials.
/// </summary>
public interface IOdosSqlDiagnostics
{
    Task<OdosSqlDiagnosticsResult> RunAsync(OdosOpeningQuery sampleQuery, CancellationToken cancellationToken);
}

internal sealed class OdosSqlDiagnostics : IOdosSqlDiagnostics
{
    private readonly ISqlConnectionFactory _connections;
    private readonly IOdosOpeningQuerySource _opening;
    private readonly IOdosBusinessLineLimitSource _limits;
    private readonly SqlStoredProcedureNames _names;
    private readonly OdosSqlConnectionOptions _appSettings;

    public OdosSqlDiagnostics(
        ISqlConnectionFactory connections,
        IOdosOpeningQuerySource opening,
        IOdosBusinessLineLimitSource limits,
        IOptions<SqlStoredProcedureNames> names,
        IOptions<OdosSqlConnectionOptions> appSettings)
    {
        _connections = connections;
        _opening = opening;
        _limits = limits;
        _names = names.Value;
        _appSettings = appSettings.Value;
    }

    public async Task<OdosSqlDiagnosticsResult> RunAsync(
        OdosOpeningQuery sampleQuery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sampleQuery);

        var connectionOpened = false;
        string? connectionError = null;
        var databaseName = _connections.DatabaseName;
        var configured = _appSettings.IsConfigured;

        if (!configured)
        {
            connectionError = MissingConfigurationReason();
        }
        else
        {
            try
            {
                await using var connection = await _connections.OpenAsync(cancellationToken);
                connectionOpened = true;
                if (!string.IsNullOrWhiteSpace(connection.Database))
                    databaseName = connection.Database;
            }
            catch (Exception ex)
            {
                connectionError = Sanitize(ex, _appSettings.DBServerPassword);
            }
        }

        var probes = new List<OdosSqlProbe>();

        if (connectionOpened)
        {
            probes.Add(await ProbeAsync(
                "GetOdosOpeningData",
                _names.GetOdosOpeningData,
                async () => _ = await _opening.QueryAsync(sampleQuery, cancellationToken)));

            probes.Add(await ProbeAsync(
                "GetBusinessLineLimit",
                _names.GetBusinessLineLimit,
                async () => _ = await _limits.GetLimitAsync(null, cancellationToken)));
        }
        else
        {
            var reason = configured ? "Connection unavailable." : "Connection not configured.";
            probes.Add(new OdosSqlProbe("GetOdosOpeningData", _names.GetOdosOpeningData, false, reason));
            probes.Add(new OdosSqlProbe("GetBusinessLineLimit", _names.GetBusinessLineLimit, false, reason));
        }

        var approved = string.Equals(
            databaseName?.Trim(),
            OdosSqlConnectionOptions.ApprovedDatabaseName,
            StringComparison.OrdinalIgnoreCase);

        return new OdosSqlDiagnosticsResult(
            configured,
            SafeTarget(),
            connectionOpened,
            databaseName ?? "(unknown)",
            approved,
            connectionError,
            probes);
    }

    private async Task<OdosSqlProbe> ProbeAsync(string capability, string procedureName, Func<Task> probe)
    {
        try
        {
            await probe();
            return new OdosSqlProbe(capability, procedureName, true, null);
        }
        catch (Exception ex)
        {
            return new OdosSqlProbe(capability, procedureName, false, Sanitize(ex, _appSettings.DBServerPassword));
        }
    }

    private string SafeTarget()
        => string.IsNullOrWhiteSpace(_appSettings.DBServerName)
            ? "(server not configured)"
            : _appSettings.SafeDescriptor;

    private string MissingConfigurationReason()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_appSettings.DBServerName)) missing.Add("AppSettings:DBServerName");
        if (string.IsNullOrWhiteSpace(_appSettings.NormalizedDatabaseName)) missing.Add("AppSettings:DBName");
        if (string.IsNullOrWhiteSpace(_appSettings.DBServerUserId)) missing.Add("AppSettings:DBServerUserId");
        if (string.IsNullOrWhiteSpace(_appSettings.DBServerPassword)) missing.Add("AppSettings:DBServerPassword (supply via user-secrets or environment variable)");

        return $"Not configured: {string.Join(", ", missing)}.";
    }

    /// <summary>
    /// Returns the exception text with any occurrence of the configured password removed.
    /// Connection strings and credentials are never surfaced.
    /// </summary>
    private static string Sanitize(Exception ex, string? password)
    {
        var message = ex.GetBaseException().Message;
        if (!string.IsNullOrEmpty(password) && message.Contains(password, StringComparison.Ordinal))
            return "Connection failed (details suppressed to avoid credential exposure).";
        return message;
    }
}

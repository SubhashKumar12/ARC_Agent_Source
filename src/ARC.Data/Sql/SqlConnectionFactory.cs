using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ARC.Data.Configuration;
using ARC.Data.Exceptions;

namespace ARC.Data.Sql;

public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenAsync(CancellationToken cancellationToken);

    /// <summary>Credential-free descriptor for logs, diagnostics and reports.</summary>
    string SafeDescriptor => "(unknown)";

    /// <summary>Database the factory will connect to. Never includes credentials.</summary>
    string DatabaseName => "(unknown)";
}

/// <summary>
/// Single place where an ARC SQL connection is constructed. Repositories never build
/// connection strings. Two configuration strategies are supported and remain replaceable:
///
/// 1. <c>AppSettings</c> SQL Server authentication (company network / ODOS estate).
/// 2. Existing <c>ArcData:Sql</c> connection string, optionally with Managed Identity.
///
/// Strategy 1 wins when fully configured; otherwise the pre-existing strategy 2 applies.
/// The password is only ever read from configuration and handed to the driver — never logged.
/// </summary>
public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly SqlStoreOptions _options;
    private readonly OdosSqlConnectionOptions _appSettings;
    private readonly ILogger<SqlConnectionFactory> _logger;

    public SqlConnectionFactory(
        IOptions<ArcDataOptions> options,
        IOptions<OdosSqlConnectionOptions>? appSettings = null,
        ILogger<SqlConnectionFactory>? logger = null)
    {
        _options = options.Value.Sql;
        _appSettings = appSettings?.Value ?? new OdosSqlConnectionOptions();
        _logger = logger ?? NullLogger<SqlConnectionFactory>.Instance;
    }

    /// <summary>True when the AppSettings SQL Server authentication path is used.</summary>
    public bool UsesAppSettingsSqlAuthentication => _appSettings.IsConfigured;

    public string DatabaseName => _appSettings.IsConfigured
        ? _appSettings.NormalizedDatabaseName
        : SafeDatabaseFromConnectionString();

    public string SafeDescriptor => _appSettings.IsConfigured
        ? _appSettings.SafeDescriptor
        : SafeDatabaseFromConnectionString();

    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = ResolveConnectionString();

        try
        {
            var connection = new SqlConnection(connectionString);

            if (!_appSettings.IsConfigured && _options.UseManagedIdentity)
            {
                var credential = new DefaultAzureCredential();
                var token = await credential.GetTokenAsync(
                    new TokenRequestContext(["https://database.windows.net/.default"]),
                    cancellationToken);
                connection.AccessToken = token.Token;
            }

            await connection.OpenAsync(cancellationToken);

            _logger.LogDebug(
                "Opened SQL connection to {SqlTarget} using {AuthMode}.",
                SafeDescriptor,
                _appSettings.IsConfigured ? "SqlServerAuthentication" : (_options.UseManagedIdentity ? "ManagedIdentity" : "ConnectionString"));

            return connection;
        }
        catch (DataAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Message deliberately excludes the connection string so credentials cannot leak.
            throw new DataAccessException($"Failed to open SQL connection to {SafeDescriptor}.", ex);
        }
    }

    private string ResolveConnectionString()
    {
        if (_appSettings.IsConfigured)
            return _appSettings.BuildConnectionString();

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new DataAccessException(
                "No SQL connection is configured. Set AppSettings:DBServerName/DBName/DBServerUserId/DBServerPassword (password outside committed source) or ArcData:Sql:ConnectionString.");
        }

        return _options.ConnectionString;
    }

    private string SafeDatabaseFromConnectionString()
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            return "(not configured)";

        try
        {
            var builder = new SqlConnectionStringBuilder(_options.ConnectionString);
            return string.IsNullOrWhiteSpace(builder.InitialCatalog)
                ? "(unknown database)"
                : builder.InitialCatalog;
        }
        catch (Exception)
        {
            return "(unparsable connection string)";
        }
    }
}

using Microsoft.Data.SqlClient;

namespace ARC.Data.Configuration;

/// <summary>
/// Central ODOS SQL Server connection configuration.
/// Bound from the existing <c>AppSettings</c> keys used by the wider ODOS estate:
/// <c>AppSettings:DBServerName</c>, <c>AppSettings:DBServerUserId</c>,
/// <c>AppSettings:DBServerPassword</c>, <c>AppSettings:DBName</c>.
///
/// The password must be supplied outside committed source (user-secrets or environment variable).
/// Repositories never build connection strings — only this type does.
/// </summary>
public sealed class OdosSqlConnectionOptions
{
    public const string SectionName = "AppSettings";

    /// <summary>The only database name ARC is approved to read ODOS data from.</summary>
    public const string ApprovedDatabaseName = "BERGER_MOBILE_APP_DB";

    public string DBServerName { get; set; } = "";
    public string DBServerUserId { get; set; } = "";
    public string DBServerPassword { get; set; } = "";
    public string DBName { get; set; } = "";

    /// <summary>Connection (login) timeout in seconds.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>Company-network SQL Server instances commonly lack a public CA chain.</summary>
    public bool TrustServerCertificate { get; set; } = true;

    public bool Encrypt { get; set; } = true;

    public string ApplicationName { get; set; } = "ARC";

    /// <summary>True when server/database/user/password are all present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(DBServerName)
        && !string.IsNullOrWhiteSpace(NormalizedDatabaseName)
        && !string.IsNullOrWhiteSpace(DBServerUserId)
        && !string.IsNullOrWhiteSpace(DBServerPassword);

    /// <summary>
    /// Database name with stray quoting/backticks removed. Backticks are not valid
    /// SQL Server identifier delimiters and must never reach the connection string.
    /// </summary>
    public string NormalizedDatabaseName =>
        (DBName ?? string.Empty).Trim().Trim('`', '"', '[', ']', '\'');

    /// <summary>Safe descriptor for logs and reports. Never contains credentials.</summary>
    public string SafeDescriptor => $"{DBServerName}/{NormalizedDatabaseName}";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DBServerName))
            throw new InvalidOperationException("AppSettings:DBServerName is not configured.");
        if (string.IsNullOrWhiteSpace(DBServerUserId))
            throw new InvalidOperationException("AppSettings:DBServerUserId is not configured.");
        if (string.IsNullOrWhiteSpace(DBServerPassword))
            throw new InvalidOperationException(
                "AppSettings:DBServerPassword is not configured. Supply it via dotnet user-secrets or an environment variable — never in committed appsettings.");

        var database = NormalizedDatabaseName;
        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("AppSettings:DBName is not configured.");

        if (!string.Equals(database, ApprovedDatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"AppSettings:DBName must be '{ApprovedDatabaseName}' for ODOS reads, but was '{database}'.");
        }
    }

    /// <summary>Builds the SQL Server connection string centrally. Result contains the password — never log it.</summary>
    public string BuildConnectionString()
    {
        Validate();

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = DBServerName.Trim(),
            InitialCatalog = NormalizedDatabaseName,
            UserID = DBServerUserId.Trim(),
            Password = DBServerPassword,
            IntegratedSecurity = false,
            ConnectTimeout = ConnectTimeoutSeconds,
            Encrypt = Encrypt,
            TrustServerCertificate = TrustServerCertificate,
            ApplicationName = ApplicationName,
            MultipleActiveResultSets = false
        };

        return builder.ConnectionString;
    }
}

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ARC.Data.Configuration;
using ARC.Data.Odos;
using ARC.Data.Sql;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.Odos;

namespace ARC.Data.Tests.Odos;

public sealed class OdosSqlDiagnosticsTests
{
    private const string Password = "not-a-real-password";

    private static OdosSqlDiagnostics Create(OdosSqlConnectionOptions appSettings, ISqlConnectionFactory factory)
        => new(
            factory,
            new ThrowingOpeningSource(),
            new ThrowingLimitSource(),
            Options.Create(new SqlStoredProcedureNames()),
            Options.Create(appSettings));

    [Fact]
    public async Task Diagnostics_ReportsMissingCredentials_WithoutAttemptingConnection()
    {
        var appSettings = new OdosSqlConnectionOptions
        {
            DBServerName = "10.160.66.37",
            DBName = OdosSqlConnectionOptions.ApprovedDatabaseName
        };
        var factory = new FailingConnectionFactory();

        var result = await Create(appSettings, factory).RunAsync(
            new OdosOpeningQuery(2026, 7),
            CancellationToken.None);

        Assert.False(result.Configured);
        Assert.False(result.ConnectionOpened);
        Assert.False(result.Passed);
        Assert.Equal(0, factory.OpenAttempts);
        Assert.Contains("AppSettings:DBServerUserId", result.ConnectionError);
        Assert.Contains("AppSettings:DBServerPassword", result.ConnectionError);
        Assert.All(result.Probes, p => Assert.False(p.Available));
    }

    [Fact]
    public async Task Diagnostics_NeverLeaksPasswordOrConnectionString()
    {
        var appSettings = new OdosSqlConnectionOptions
        {
            DBServerName = "10.160.66.37",
            DBName = OdosSqlConnectionOptions.ApprovedDatabaseName,
            DBServerUserId = "arc_reader",
            DBServerPassword = Password
        };

        var result = await Create(appSettings, new FailingConnectionFactory(includePasswordInError: Password))
            .RunAsync(new OdosOpeningQuery(2026, 7), CancellationToken.None);

        Assert.True(result.Configured);
        Assert.False(result.ConnectionOpened);
        Assert.NotNull(result.ConnectionError);
        Assert.DoesNotContain(Password, result.ConnectionError);
        Assert.DoesNotContain(Password, result.ConfiguredTarget);
        Assert.Equal("10.160.66.37/BERGER_MOBILE_APP_DB", result.ConfiguredTarget);
    }

    [Fact]
    public async Task Diagnostics_ProbesBothApprovedProcedureNames()
    {
        var appSettings = new OdosSqlConnectionOptions
        {
            DBServerName = "10.160.66.37",
            DBName = OdosSqlConnectionOptions.ApprovedDatabaseName
        };

        var result = await Create(appSettings, new FailingConnectionFactory()).RunAsync(
            new OdosOpeningQuery(2026, 7),
            CancellationToken.None);

        Assert.Collection(result.Probes,
            p => Assert.Equal("GetOdosOpeningData", p.Capability),
            p => Assert.Equal("GetBusinessLineLimit", p.Capability));

        Assert.Equal("ODOS.usp_GetOpeningDataForArc", result.Probes[0].ProcedureName);
        Assert.Equal("ODOS.usp_GetBusinessLineLimit", result.Probes[1].ProcedureName);
    }

    private sealed class FailingConnectionFactory : ISqlConnectionFactory
    {
        private readonly string? _passwordInError;

        public FailingConnectionFactory(string? includePasswordInError = null)
            => _passwordInError = includePasswordInError;

        public int OpenAttempts { get; private set; }

        public string SafeDescriptor => "10.160.66.37/BERGER_MOBILE_APP_DB";
        public string DatabaseName => "(not connected)";

        public Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
        {
            OpenAttempts++;
            var message = _passwordInError is null
                ? "Login failed."
                : $"Login failed for connection string Password={_passwordInError};";
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ThrowingOpeningSource : IOdosOpeningQuerySource
    {
        public Task<IReadOnlyList<OdosOpeningSnapshot>> QueryAsync(
            OdosOpeningQuery query,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("Should not be reached without a connection.");
    }

    private sealed class ThrowingLimitSource : IOdosBusinessLineLimitSource
    {
        public Task<OdosBusinessLineLimit?> GetLimitAsync(string? businessLine, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Should not be reached without a connection.");
    }
}

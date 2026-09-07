using Microsoft.Data.SqlClient;
using ARC.Data.Sql;

namespace ARC.Api.Tests.Fakes;

/// <summary>
/// Test fake for SQL connections. Does not connect to real database.
/// </summary>
public sealed class FakeSqlConnectionFactory : ISqlConnectionFactory
{
    public Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Test fake: SQL connection not available in test environment.");
    }
}

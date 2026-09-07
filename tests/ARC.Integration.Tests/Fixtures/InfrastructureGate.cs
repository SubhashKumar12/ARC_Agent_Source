using Microsoft.Azure.Cosmos;
using Microsoft.Data.SqlClient;

namespace ARC.Integration.Tests.Fixtures;

/// <summary>
/// Resolves Cosmos + SQL for AC#2. Fails loudly when durable infrastructure is unavailable.
/// Does not fall back to in-memory MAF checkpoints.
/// </summary>
internal static class InfrastructureGate
{
    /// <summary>Well-known local Cosmos Emulator key (public Microsoft documentation value, not a secret).</summary>
    public const string EmulatorAccountKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    public static string ResolveSqlConnectionString()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ARC_SQL_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        return "Server=(localdb)\\MSSQLLocalDB;Database=ARC_Integration;Trusted_Connection=True;TrustServerCertificate=True;";
    }

    public static string ResolveCosmosConnectionString()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ARC_COSMOS_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        // Prefer emulator when no env override is provided.
        return $"AccountEndpoint=https://localhost:8081/;AccountKey={EmulatorAccountKey}";
    }

    public static async Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        var sql = ResolveSqlConnectionString();
        var cosmos = ResolveCosmosConnectionString();

        try
        {
            TryStartLocalDb(sql);
            var builder = new SqlConnectionStringBuilder(sql);
            builder.InitialCatalog = "master";
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var ping = connection.CreateCommand();
            ping.CommandText = "SELECT 1";
            await ping.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Assert.Fail(
                "BLOCKED BY ENVIRONMENT: SQL is unavailable for AC#2. " +
                "Set ARC_SQL_CONNECTION_STRING or start LocalDB (sqllocaldb start MSSQLLocalDB). " +
                $"Detail: {ex.Message}");
        }

        try
        {
            using var client = CreateCosmosClient(cosmos);
            await client.ReadAccountAsync();
        }
        catch (Exception ex)
        {
            Assert.Fail(
                "BLOCKED BY ENVIRONMENT: Cosmos DB is unavailable for AC#2 durable resume. " +
                "Start the Azure Cosmos DB Emulator on https://localhost:8081 " +
                "or set ARC_COSMOS_CONNECTION_STRING to a DEV account. " +
                "In-memory checkpoints are not accepted as AC#2 proof. " +
                $"Detail: {ex.Message}");
        }
    }

    public static async Task EnsureSqlAvailableAsync(CancellationToken cancellationToken)
    {
        var sql = ResolveSqlConnectionString();
        try
        {
            TryStartLocalDb(sql);
            var builder = new SqlConnectionStringBuilder(sql);
            builder.InitialCatalog = "master";
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var ping = connection.CreateCommand();
            ping.CommandText = "SELECT 1";
            await ping.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Assert.Fail(
                "BLOCKED BY ENVIRONMENT: SQL is unavailable for SP2 identity persistence. " +
                "Set ARC_SQL_CONNECTION_STRING or start LocalDB (sqllocaldb start MSSQLLocalDB). " +
                $"Detail: {ex.Message}");
        }
    }

    private static void TryStartLocalDb(string connectionString)
    {
        if (!connectionString.Contains("(localdb)", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains("localdb\\", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sqllocaldb",
                Arguments = "start MSSQLLocalDB",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            process?.WaitForExit(15_000);
        }
        catch
        {
            // Availability is proven by the subsequent SQL open; start is best-effort.
        }
    }

    public static CosmosClient CreateCosmosClient(string connectionString)
    {
        var local = connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                    || connectionString.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);
        if (!local)
            return new CosmosClient(connectionString);

        return new CosmosClient(connectionString, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            })
        });
    }
}

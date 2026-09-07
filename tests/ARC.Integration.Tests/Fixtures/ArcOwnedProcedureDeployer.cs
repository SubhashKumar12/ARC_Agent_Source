using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ARC.Integration.Tests.Fixtures;

/// <summary>
/// Deploys Phase 11P ARC-owned procedures to LocalDB / test SQL.
/// Transforms <c>CREATE OR ALTER PROCEDURE</c> to stub+ALTER for SQL Server 2016 RTM/RC
/// (CREATE OR ALTER requires SQL Server 2016 SP1+). Procedure bodies are unchanged.
/// </summary>
internal static class ArcOwnedProcedureDeployer
{
    private static readonly Regex CreateOrAlterHeader = new(
        @"CREATE\s+OR\s+ALTER\s+PROCEDURE\s+(dbo\.\w+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static async Task DeployPhase11pScriptAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var scriptPath = Path.Combine(FindRepoRoot(), "SQL", "ARC", "PHASE_11P_ARC_OWNED_STORED_PROCEDURES.sql");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("Phase 11P ARC-owned SP script not found.", scriptPath);

        var script = StripBlockComments(await File.ReadAllTextAsync(scriptPath, cancellationToken));
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var batch in SplitGoBatches(script))
        {
            var text = batch.Trim();
            if (!ContainsExecutableSql(text))
                continue;

            var match = CreateOrAlterHeader.Match(text);
            if (match.Success)
            {
                var procedure = match.Groups[1].Value;
                await EnsureProcedureStubAsync(connection, procedure, cancellationToken);
                text = CreateOrAlterHeader.Replace(text, $"ALTER PROCEDURE {procedure}");
            }

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = text;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string StripBlockComments(string script)
        => Regex.Replace(script, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    private static bool ContainsExecutableSql(string batch)
    {
        foreach (var line in batch.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal))
                continue;
            return true;
        }

        return false;
    }

    public static async Task<int> CountDeployedProceduresAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM sys.procedures p
            INNER JOIN sys.schemas s ON s.schema_id = p.schema_id
            WHERE s.name = N'dbo'
              AND p.name LIKE N'usp[_]%'
              AND p.name IN (
                  N'usp_UpsertRecoveryCaseIndex', N'usp_GetRecoveryCaseIndex', N'usp_ListRecoveryCasesByCycle',
                  N'usp_GetRankedWorklist', N'usp_GetDealerSourceMapping', N'usp_SaveResolvedMapping',
                  N'usp_FindDealerUrnsByIdentifier', N'usp_FindDealerUrnsByAlias', N'usp_SaveDealerAlias',
                  N'usp_SaveGateDecision', N'usp_ListGateDecisions', N'usp_UpsertPtpRecord', N'usp_GetPtpRecord',
                  N'usp_ListPtpCandidatesByCycleDealer', N'usp_ListPtpCommittedByDealer', N'usp_ListPtpCandidatesByStatus',
                  N'usp_UpsertVisitPlan', N'usp_GetVisitPlan', N'usp_ListVisitPlansByCycleTsi',
                  N'usp_UpsertPtpChase', N'usp_GetPtpChase', N'usp_ListPtpChasesByCycleStatus', N'usp_ListPtpChasesByTsiStatus'
              );
            """;
        return (int)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    public static async Task<bool> VisitPlanLineInputTypeExistsAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT CASE WHEN TYPE_ID(N'dbo.VisitPlanLineInput') IS NOT NULL THEN 1 ELSE 0 END;
            """;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task EnsureProcedureStubAsync(
        SqlConnection connection,
        string procedureName,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            IF OBJECT_ID(N'{procedureName}', N'P') IS NULL
                EXEC(N'CREATE PROCEDURE {procedureName} AS RETURN 0;');
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IEnumerable<string> SplitGoBatches(string script)
    {
        var lines = script.Split('\n');
        var batch = new List<string>();
        foreach (var line in lines)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (batch.Count > 0)
                {
                    yield return string.Join('\n', batch);
                    batch.Clear();
                }
            }
            else
            {
                batch.Add(line);
            }
        }

        if (batch.Count > 0)
            yield return string.Join('\n', batch);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ARC.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root (ARC.sln).");
    }
}

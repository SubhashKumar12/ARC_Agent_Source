using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ARC.Data.Configuration;
using ARC.Data.Odos;
using ARC.Domain.Odos;

namespace ARC.Cli.Commands;

/// <summary>
/// Phase 11D read-only ODOS SQL commands: <c>sql-check</c> and <c>odos-dry-run</c>.
/// Both execute only the two approved stored procedures. No writes, no notices,
/// no legal action, no workflow state. Credentials are never printed.
/// </summary>
internal static class OdosSqlCommands
{
    public static async Task<int> RunSqlCheckAsync(string[] args, CancellationToken cancellationToken)
    {
        using var host = BuildHost(args);
        var diagnostics = host.Services.GetRequiredService<IOdosSqlDiagnostics>();

        // Sample parameters must be explicit — ARC never infers a production period.
        var sample = ParseQuery(args, out var parseError);
        if (sample is null)
        {
            Console.WriteLine($"SQL Connection: FAIL  ({parseError})");
            Console.WriteLine("Supply an explicit sample period, e.g. --year 2026 --month 07");
            return 1;
        }

        Console.WriteLine("ARC CLI — ODOS SQL connectivity check (read-only, approved SPs only).");
        Console.WriteLine();

        var result = await diagnostics.RunAsync(sample, cancellationToken);

        Console.WriteLine($"Target: {result.ConfiguredTarget}");
        Console.WriteLine($"SQL Connection: {(result.ConnectionOpened ? "PASS" : "FAIL")}");
        if (!string.IsNullOrWhiteSpace(result.ConnectionError))
            Console.WriteLine($"  reason: {result.ConnectionError}");
        Console.WriteLine($"Database: {result.DatabaseName}{(result.DatabaseApproved ? "" : "  (NOT the approved database)")}");

        foreach (var probe in result.Probes)
        {
            Console.WriteLine($"{probe.Capability}: {(probe.Available ? "AVAILABLE" : "UNAVAILABLE")}");
            if (!probe.Available && !string.IsNullOrWhiteSpace(probe.Detail))
                Console.WriteLine($"  reason: {probe.Detail}");
        }

        Console.WriteLine();
        if (result.Passed)
        {
            Console.WriteLine("PASS  ODOS SQL read path reachable.");
        }
        else if (!result.Configured)
        {
            Console.WriteLine("LIVE SQL VERIFICATION PENDING — credentials not supplied on this machine.");
            Console.WriteLine("Set them without committing secrets:");
            Console.WriteLine("  dotnet user-secrets --project src/ARC.Cli set \"AppSettings:DBServerUserId\" \"<user>\"");
            Console.WriteLine("  dotnet user-secrets --project src/ARC.Cli set \"AppSettings:DBServerPassword\" \"<password>\"");
        }
        else
        {
            Console.WriteLine("FAIL  ODOS SQL read path not reachable. LIVE SQL VERIFICATION PENDING.");
        }

        return result.Passed ? 0 : 1;
    }

    public static async Task<int> RunOdosDryRunAsync(string[] args, CancellationToken cancellationToken)
    {
        using var host = BuildHost(args);
        var dryRun = host.Services.GetRequiredService<IOdosDryRunService>();

        var query = ParseQuery(args, out var parseError);
        if (query is null)
        {
            Console.WriteLine($"FAIL  {parseError}");
            Console.WriteLine("Usage: dotnet run --project src/ARC.Cli -- odos-dry-run --year 2026 --month 07 [--depot XXX] [--dealer YYYY]");
            return 1;
        }

        var sampleSize = ParseInt(args, "--sample") ?? 10;

        Console.WriteLine("ARC CLI — ODOS production dry run (READ-ONLY).");
        Console.WriteLine("No writes, no notice, no legal action, no workflow state.");
        Console.WriteLine($"Period: {query.Year:D4}-{query.Month:D2}  Depot: {query.DepotCode ?? "(all)"}  Dealer: {query.DealerCode ?? "(all)"}");
        Console.WriteLine();

        OdosDryRunResult result;
        try
        {
            result = await dryRun.RunAsync(query, sampleSize, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL  ODOS read failed: {ex.GetBaseException().Message}");
            Console.WriteLine("LIVE SQL VERIFICATION PENDING.");
            return 1;
        }

        Console.WriteLine($"Rows returned: {result.RowsReturned}   Sample shown: {result.SampleSize}");
        Console.WriteLine($"Default business limit (from SP): {result.DefaultBusinessLimit:N2}");
        Console.WriteLine();

        Console.WriteLine("Depot  Dealer      BizLine  CurrentOS        OS0          OS1          OS2          OS3          OS4          Limit        Eligible");
        Console.WriteLine(new string('-', 130));
        foreach (var line in result.Sample)
        {
            Console.WriteLine(string.Join("  ", new[]
            {
                line.DepotCode.PadRight(5),
                line.DealerCode.PadRight(10),
                (line.BusinessLine ?? "-").PadRight(7),
                line.CurrentOutstanding.ToString("N2").PadLeft(13),
                line.OsAmt0.ToString("N2").PadLeft(11),
                line.OsAmt1.ToString("N2").PadLeft(11),
                line.OsAmt2.ToString("N2").PadLeft(11),
                line.OsAmt3.ToString("N2").PadLeft(11),
                line.OsAmt4.ToString("N2").PadLeft(11),
                line.BusinessLimit.ToString("N2").PadLeft(11),
                line.EligibleByCurrentOdosLimit ? "Yes" : "No"
            }));
        }

        Console.WriteLine();
        Console.WriteLine("Eligibility rule: od_os_amt_updt > applicable business-line limit (SP-supplied).");
        Console.WriteLine("PASS  Dry run complete (read-only).");
        return 0;
    }

    private static IHost BuildHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // dotnet run sets the content root to the invocation directory, so anchor
        // non-secret configuration to the assembly location instead.
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        builder.Configuration.AddUserSecrets(typeof(OdosSqlCommands).Assembly, optional: true);
        builder.Configuration.AddEnvironmentVariables();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddArcOdosSqlReadPath(builder.Configuration);
        return builder.Build();
    }

    private static OdosOpeningQuery? ParseQuery(string[] args, out string? error)
    {
        error = null;
        var year = ParseInt(args, "--year");
        var month = ParseInt(args, "--month");

        if (year is null || month is null)
        {
            error = "An explicit --year and --month are required; ARC does not infer a production period.";
            return null;
        }

        try
        {
            return new OdosOpeningQuery(
                year.Value,
                month.Value,
                ParseValue(args, "--depot"),
                ParseValue(args, "--dealer"));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static string? ParseValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(args[i + 1]) ? null : args[i + 1].Trim();
        }
        return null;
    }

    private static int? ParseInt(string[] args, string name)
        => int.TryParse(ParseValue(args, name), out var parsed) ? parsed : null;
}

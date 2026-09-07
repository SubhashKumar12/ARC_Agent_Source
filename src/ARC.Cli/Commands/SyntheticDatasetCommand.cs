using Microsoft.Extensions.Logging;
using ARC.Data.Synthetic;
using ARC.Tools.Reconciliation;

namespace ARC.Cli.Commands;

/// <summary>Validates the Phase 11B synthetic assignment dataset. Not part of S1–S9.</summary>
internal sealed class SyntheticDatasetCommand
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("ARC CLI — synthetic assignment dataset (Phase 11B).");
        Console.WriteLine($"Classification: {SyntheticAssignmentLabels.Marker}");
        Console.WriteLine();

        var options = new SyntheticDatasetOptions();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var dataset = new SyntheticDatasetGenerator().Generate(options);
        sw.Stop();

        var store = new SyntheticDatasetStore(dataset);
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        var tool = new ReconciliationTool(store, store, loggerFactory.CreateLogger<ReconciliationTool>());

        var r6Urn = dataset.R6LineageCase.DealerUrn;
        var exposure = await tool.ComputeNetExposureAsync(
            new ComputeNetExposureRequest(r6Urn.Value, dataset.AsOf, "synth-r6", "synth"),
            cancellationToken);

        Console.WriteLine($"Seed:              {dataset.Seed}");
        Console.WriteLine($"Dealers:           {dataset.Dealers.Count}");
        Console.WriteLine($"Opening rows:      {dataset.OpeningHistory.Count}");
        Console.WriteLine($"History months:    {dataset.HistoryMonthCount}");
        Console.WriteLine($"Generation ms:     {sw.ElapsedMilliseconds}");
        Console.WriteLine($"Scenario tags:     {string.Join(", ", dataset.ScenarioDealers.Keys.OrderBy(k => (int)k))}");
        Console.WriteLine($"Mother links:      {dataset.MotherAccountLinks.Count}");
        Console.WriteLine($"R6 net exposure:   {dataset.R6LineageCase.ReconciledNetExposure:N2}");
        Console.WriteLine($"R6 drafted notice: {dataset.R6LineageCase.DraftedNoticeAmount:N2}");
        Console.WriteLine($"A1 R6 live net:    {exposure.Exposure.NetRecoverableExposure.Amount:N2}");
        Console.WriteLine($"R6 lineage rows:   {dataset.R6LineageCase.SourceFacts.Count}");
        Console.WriteLine();

        var ok =
            dataset.Dealers.Count is >= 2500 and <= 2510
            && dataset.HistoryMonthCount == 12
            && dataset.ScenarioDealers.Count >= 10
            && dataset.R6LineageCase.DraftedNoticeAmount == dataset.R6LineageCase.ReconciledNetExposure
            && exposure.Exposure.NetRecoverableExposure.Amount == dataset.R6LineageCase.DraftedNoticeAmount;

        Console.WriteLine(ok ? "PASS  Synthetic dataset checks." : "FAIL  Synthetic dataset checks.");
        return ok ? 0 : 1;
    }
}

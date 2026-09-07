using Microsoft.Extensions.Logging;
using ARC.Data.A1;
using ARC.Data.Synthetic;
using ARC.Domain.Enums;
using ARC.Domain.Odos;
using ARC.Tools.Reconciliation;

namespace ARC.Agents.Tests.Synthetic;

public sealed class SyntheticDatasetPhase11BTests
{
    private const int Seed = 112_026_11;

    [Fact]
    public void Generate_IsDeterministic_ForFixedSeed()
    {
        var a = new SyntheticDatasetGenerator().Generate(new SyntheticDatasetOptions { Seed = Seed });
        var b = new SyntheticDatasetGenerator().Generate(new SyntheticDatasetOptions { Seed = Seed });

        Assert.Equal(a.Dealers.Count, b.Dealers.Count);
        Assert.Equal(a.OpeningHistory.Count, b.OpeningHistory.Count);
        Assert.Equal(a.R6LineageCase.DraftedNoticeAmount, b.R6LineageCase.DraftedNoticeAmount);
        Assert.Equal(a.OpeningHistory[0].Snapshot.OsAmtUpdt, b.OpeningHistory[0].Snapshot.OsAmtUpdt);
        Assert.Equal(a.OpeningHistory[^1].Snapshot.OsAmtUpdt, b.OpeningHistory[^1].Snapshot.OsAmtUpdt);
        Assert.Equal(
            a.Adjustments.Sum(x => x.Amount.Amount),
            b.Adjustments.Sum(x => x.Amount.Amount));
    }

    [Fact]
    public void Generate_ProducesApprox2500Dealers_And12Months()
    {
        var ds = new SyntheticDatasetGenerator().Generate(new SyntheticDatasetOptions { Seed = Seed });
        Assert.InRange(ds.Dealers.Count, 2500, 2510);
        Assert.Equal(12, ds.HistoryMonthCount);
        Assert.Equal(ds.Dealers.Count * 12, ds.OpeningHistory.Count);
        Assert.Equal(SyntheticAssignmentLabels.Marker, ds.DataClassification);
    }

    [Fact]
    public void Generate_IncludesAllS1ToS9_AndR6()
    {
        var ds = new SyntheticDatasetGenerator().Generate(new SyntheticDatasetOptions { Seed = Seed });
        foreach (SyntheticScenarioTag tag in Enum.GetValues<SyntheticScenarioTag>())
        {
            if (tag == SyntheticScenarioTag.None)
                continue;
            Assert.True(ds.ScenarioDealers.ContainsKey(tag), $"Missing scenario {tag}");
        }
    }

    [Fact]
    public void Generate_MotherAccountDuplicateIdentityScenario()
    {
        var ds = new SyntheticDatasetGenerator().Generate(new SyntheticDatasetOptions { Seed = Seed });
        Assert.Contains(ds.MotherAccountLinks, l => l.MotherAccountCode == "MOTHER-S9");
        var sapChild = ds.IdentityMappings.Where(m => m.SourceIdentifier == "SAP-D9CHILD").ToList();
        Assert.True(sapChild.Count >= 2, "Mother-account duplicate identity requires ≥2 mappings for child SAP.");
        Assert.Contains(sapChild, m => m.CanonicalUrn.Value.Contains("s9", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Generate_R1bHighCreditScenario()
    {
        var ds = new SyntheticDatasetGenerator().Generate(new SyntheticDatasetOptions { Seed = Seed });
        var s2 = ds.ScenarioDealers[SyntheticScenarioTag.S2_HighCreditR1b];
        var credit = ds.Adjustments.Where(a => a.DealerUrn.Equals(s2.CanonicalUrn) && a.DocumentType.Contains("Credit", StringComparison.OrdinalIgnoreCase)).Sum(a => a.Amount.Amount);
        var gross = ds.OpeningHistory
            .Where(o => o.DealerUrn.Equals(s2.CanonicalUrn))
            .OrderByDescending(o => o.Snapshot.Year).ThenByDescending(o => o.Snapshot.Month)
            .First().Snapshot.OsAmtUpdt;
        Assert.True(credit / gross > 0.40m, $"R1b credit ratio {credit / gross:P0}");
    }

    [Fact]
    public void Generate_DisputeAndMoratoriumHolds()
    {
        var ds = new SyntheticDatasetGenerator().Generate(new SyntheticDatasetOptions { Seed = Seed });
        var s6 = ds.ScenarioDealers[SyntheticScenarioTag.S6_DisputeHold];
        Assert.Contains(ds.Disputes, d => d.DealerUrn.Equals(s6.CanonicalUrn) && d.Status == DisputeStatus.UnderReview);

        var s7 = ds.ScenarioDealers[SyntheticScenarioTag.S7_MoratoriumHold];
        Assert.True(s7.UnderInsolvencyMoratorium);
    }

    [Fact]
    public void Generate_QualifyingAndNonQualifyingCheques()
    {
        var ds = new SyntheticDatasetGenerator().Generate(new SyntheticDatasetOptions { Seed = Seed });
        Assert.Contains(ds.Section138Facts, f => f.QualifyingBounce);
        Assert.Contains(ds.Section138Facts, f => !f.QualifyingBounce);
        Assert.Contains(ds.ReturnMemos, m => m.ReturnReasonCode == "FUNDS_INSUFFICIENT");
        Assert.Contains(ds.ReturnMemos, m => m.ReturnReasonCode == "SIGNATURE_MISMATCH");
    }

    [Fact]
    public void Generate_LimitationT2_And_LowConfidencePtp()
    {
        var ds = new SyntheticDatasetGenerator().Generate(new SyntheticDatasetOptions { Seed = Seed });
        Assert.True(ds.ScenarioDealers.ContainsKey(SyntheticScenarioTag.S5_LimitationT2));
        var s8 = ds.ScenarioDealers[SyntheticScenarioTag.S8_LowConfidencePtp];
        var ptp = ds.Ptps.Single(p => p.DealerUrn.Equals(s8.CanonicalUrn));
        Assert.False(ptp.ConfirmedByTsi);
        Assert.True(ptp.Confidence < 0.80m);
    }

    [Fact]
    public void Generate_R6Lineage_SourceToNoticeAmount()
    {
        var ds = new SyntheticDatasetGenerator().Generate(new SyntheticDatasetOptions { Seed = Seed });
        var r6 = ds.R6LineageCase;
        Assert.NotEmpty(r6.SourceFacts);
        Assert.Equal(r6.ReconciledNetExposure, r6.DraftedNoticeAmount);
        Assert.Equal("Issue", r6.Decision);
        Assert.True(r6.ReconciledNetExposure > 0m);
    }

    [Fact]
    public async Task A1_ReconciliationTool_ConsumesSyntheticStore_WithoutProviderCoupling()
    {
        var store = SyntheticDatasetStore.CreateDefault(Seed);
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        var tool = new ReconciliationTool(store, store, loggerFactory.CreateLogger<ReconciliationTool>());

        var r6 = store.R6LineageCase;
        var result = await tool.ComputeNetExposureAsync(
            new ComputeNetExposureRequest(r6.DealerUrn.Value, store.Dataset.AsOf, null, null),
            CancellationToken.None);

        Assert.Equal(r6.DraftedNoticeAmount, result.Exposure.NetRecoverableExposure.Amount);
        Assert.Equal(r6.SourceFacts.Count, result.Exposure.Lineage.Count);
        Assert.True(result.LedgerLineCount >= 2);
    }

    [Fact]
    public void BusinessLineLimit_IsNotHardCoded5000()
    {
        var provider = new ConfigurableBusinessLineLimitProvider(new BusinessLineLimitOptions
        {
            DefaultLimit = BusinessLineLimitOptions.SyntheticAssignmentDefaultLimit
        });
        Assert.NotEqual(5000m, provider.GetDefaultLimit());
        Assert.True(OdosEligibility.ExceedsOutstandingLimit(25_000m, "DECO", provider));
        Assert.False(OdosEligibility.ExceedsOutstandingLimit(100m, null, provider));
    }

    [Fact]
    public void OpeningProjector_DoesNotRecalculateAgeingFromDates()
    {
        var snap = new OdosOpeningSnapshot(
            "BPIL", 2026, 3, "Depot", "D00001", "DECO", "DLR", "BT",
            "TRX-1", new DateOnly(2026, 1, 1), "OS", "DOC-1",
            OsAmt0: 40m, OsAmt1: 30m, OsAmt2: 20m, OsAmt3: 5m, OsAmt4: 5m,
            OsAmtUpdt: 100m);

        var lines = OdosOpeningLedgerProjector.Project(
            new Domain.ValueObjects.DealerUrn("dealer:test"),
            snap,
            adjustments: null,
            openingIsSynthetic: true);

        Assert.Single(lines);
        Assert.Equal(100m, lines[0].Amount.Amount);
        // Buckets retained on snapshot only; projector uses od_os_amt_updt.
        Assert.Equal(100m, snap.OsAmt0 + snap.OsAmt1 + snap.OsAmt2 + snap.OsAmt3 + snap.OsAmt4);
    }
}

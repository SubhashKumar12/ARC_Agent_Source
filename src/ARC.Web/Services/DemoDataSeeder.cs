using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Tools.Evidence;

namespace ARC.Web.Services;

/// <summary>
/// Seeds demo data for S1-S9 scenarios.
/// Adapted from CLI ScenarioSeed with correct constructors.
/// </summary>
public sealed class DemoDataSeeder
{
    private readonly InMemoryArcStore _store;

    public DemoDataSeeder(InMemoryArcStore store)
    {
        _store = store;
    }

    public async Task SeedScenarioAsync(string scenarioId, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Seeding is synchronous but keeping async signature for consistency
        
        switch (scenarioId.ToUpperInvariant())
        {
            case "S1":
                SeedS1();
                break;
            case "S2":
                SeedS2();
                break;
            case "S3":
                SeedS3();
                break;
            case "S4":
                SeedS4();
                break;
            case "S5":
                SeedS5();
                break;
            case "S6":
                SeedS6();
                break;
            case "S7":
                SeedS7();
                break;
            case "S8":
                SeedS8();
                break;
            case "S9":
                SeedS9();
                break;
        }
    }

    private void SeedS1()
    {
        const string urn = "dealer:s1";
        _store.SeedDealer(CreateDealer(urn));
        _store.SeedLedger(CreateInvoice(urn, 100_000m, "SAP-FI-AR", "INV-S1"));
    }

    private void SeedS2()
    {
        const string urn = "dealer:s2";
        _store.SeedDealer(CreateDealer(urn));
        _store.SeedLedger(
            CreateInvoice(urn, 100_000m, "SAP-FI-AR", "INV-S2"),
            CreateCreditNote(urn, 77_000m, "SAP-FI-AR", "CN-S2"));
    }

    private void SeedS3()
    {
        const string urn = "dealer:s3";
        _store.SeedDealer(CreateDealer(urn));
        _store.SeedLedger(CreateInvoice(urn, 100_000m, "SAP-FI-AR", "INV-S3"));
        _store.SeedCheque(CreateBouncedCheque(urn));
        _store.SeedMemo(CreateMemo(urn, "FUNDS_INSUFFICIENT"));
        SeedEvidence(urn);
    }

    private void SeedS4()
    {
        const string urn = "dealer:s4";
        _store.SeedDealer(CreateDealer(urn));
        _store.SeedLedger(CreateInvoice(urn, 150_000m, "SAP-FI-AR", "INV-S4"));
    }

    private void SeedS5()
    {
        const string urn = "dealer:s5";
        _store.SeedDealer(CreateDealer(urn));
        _store.SeedLedger(CreateInvoice(urn, 50_000m, "SAP-FI-AR", "INV-S5"));
    }

    private void SeedS6()
    {
        const string urn = "dealer:s6";
        _store.SeedDealer(CreateDealer(urn));
        _store.SeedLedger(CreateInvoice(urn, 300_000m, "SAP-FI-AR", "INV-S6"));
    }

    private void SeedS7()
    {
        const string urn = "dealer:s7";
        _store.SeedDealer(CreateDealer(urn));
        _store.SeedLedger(CreateInvoice(urn, 75_000m, "SAP-FI-AR", "INV-S7"));
    }

    private void SeedS8()
    {
        const string urn = "dealer:s8";
        _store.SeedDealer(CreateDealer(urn));
        _store.SeedLedger(CreateInvoice(urn, 180_000m, "SAP-FI-AR", "INV-S8"));
    }

    private void SeedS9()
    {
        const string urn = "dealer:s9";
        _store.SeedDealer(CreateDealer(urn));
        _store.SeedLedger(CreateInvoice(urn, 120_000m, "SAP-FI-AR", "INV-S9"));
    }

    // Helper methods using correct constructors from CLI ScenarioSeed

    private static Dealer CreateDealer(
        string urn,
        bool moratorium = false,
        string? sap = "SAP-1001",
        string? portal = "PORTAL-1001",
        string? tsi = "tsi.west@paintco.local",
        string? appId = null)
        => new(
            new DealerUrn(urn),
            moratorium,
            sapCode: sap,
            portalId: portal,
            depot: "Mumbai-Andheri",
            region: "West",
            coveringTsi: tsi,
            appId: appId);

    private static LedgerPosition CreateInvoice(string urn, decimal amount, string sourceSystem, string sourceKey)
        => CreateLine(urn, "Invoice", amount, sourceSystem, sourceKey);

    private static LedgerPosition CreateCreditNote(string urn, decimal amount, string sourceSystem, string sourceKey)
        => CreateLine(urn, "CreditNote", amount, sourceSystem, sourceKey);

    private static LedgerPosition CreateLine(
        string urn,
        string documentType,
        decimal amount,
        string sourceSystem,
        string sourceKey)
    {
        var dealer = new DealerUrn(urn);
        var posted = new DateOnly(2025, 11, 15);
        return new LedgerPosition(
            dealer,
            documentType,
            dueDate: new DateOnly(2025, 12, 1),
            postedOn: posted,
            amount: new Money(amount),
            lineage: new LineItemRef(sourceSystem, "BSEG", sourceKey, amount, posted));
    }

    private static SecurityCheque CreateBouncedCheque(string urn, string chequeNumber = "CHQ-9001")
        => new(
            new DealerUrn(urn),
            chequeNumber,
            new Money(100_000m),
            ChequeStatus.Bounced,
            micr: "400002000",
            depositDate: new DateOnly(2026, 1, 1),
            validityEnd: new DateOnly(2027, 1, 1));

    private static ChequeReturnMemo CreateMemo(string urn, string reason, string chequeNumber = "CHQ-9001")
        => new(
            new DealerUrn(urn),
            chequeNumber,
            reason,
            memoIssueDate: new DateOnly(2026, 1, 1),
            memoReceivedDate: new DateOnly(2026, 1, 1));

    private void SeedEvidence(string urn)
    {
        foreach (var type in EvidenceCaseFileTool.RequiredSection138Artefacts)
        {
            var location = $"legal-worm/{urn}/{type}.pdf";
            _store.SeedEvidence(location);
        }
    }
}

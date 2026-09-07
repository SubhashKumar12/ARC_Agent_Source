using ARC.Cli.Fakes;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Tools.Evidence;

namespace ARC.Cli.Scenarios;

internal static class ScenarioSeed
{
    public static readonly DateOnly AsOfOdos = new(2026, 3, 1);
    public static readonly DateOnly AsOfS138Open = new(2026, 1, 25);
    public static readonly DateOnly AsOfT2 = new(2026, 2, 22);
    public static readonly DateOnly NoticeServed = new(2026, 1, 10);
    public static readonly DateOnly MemoReceived = new(2026, 1, 1);

    public static Dealer Dealer(
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

    public static LedgerPosition Invoice(string urn, decimal amount, string sourceSystem, string sourceKey)
        => Line(urn, "Invoice", amount, sourceSystem, sourceKey);

    public static LedgerPosition CreditNote(string urn, decimal amount, string sourceSystem, string sourceKey)
        => Line(urn, "CreditNote", amount, sourceSystem, sourceKey);

    public static LedgerPosition Line(
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

    public static SecurityCheque BouncedCheque(string urn, string chequeNumber = "CHQ-9001")
        => new(
            new DealerUrn(urn),
            chequeNumber,
            new Money(100_000m),
            ChequeStatus.Bounced,
            micr: "400002000",
            depositDate: MemoReceived,
            validityEnd: new DateOnly(2027, 1, 1));

    public static ChequeReturnMemo Memo(string urn, string reason, string chequeNumber = "CHQ-9001")
        => new(
            new DealerUrn(urn),
            chequeNumber,
            reason,
            memoIssueDate: MemoReceived,
            memoReceivedDate: MemoReceived);

    public static DemandNotice Notice(string urn, string cycleId)
        => new(
            new DealerUrn(urn),
            new CycleId(cycleId),
            issuedOn: NoticeServed,
            claimAmount: new Money(100_000m),
            servedOn: NoticeServed);

    public static IReadOnlyList<EvidenceItem> Section138Evidence(string urn)
    {
        var items = new List<EvidenceItem>();
        foreach (var type in EvidenceCaseFileTool.RequiredSection138Artefacts)
        {
            var location = $"legal-worm/{urn}/{type}.pdf";
            items.Add(new EvidenceItem(type, location));
        }

        return items;
    }

    public static void SeedEvidence(InMemoryArcStore store, string urn)
    {
        foreach (var item in Section138Evidence(urn))
            store.SeedEvidence(item.Location);
    }
}

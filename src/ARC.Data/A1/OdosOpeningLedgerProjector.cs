using ARC.Domain.Entities;
using ARC.Domain.Odos;
using ARC.Domain.ValueObjects;

namespace ARC.Data.A1;

/// <summary>
/// Projects ODOS opening snapshots + optional adjustment facts into domain <see cref="LedgerPosition"/>
/// lines for A1 (<c>ComputeNetExposure</c>). Does not recalculate ageing from dates —
/// bucket amounts on the snapshot are carried only as Oracle-supplied metadata on lineage keys.
/// </summary>
public static class OdosOpeningLedgerProjector
{
    public const string ProductionOpeningSourceSystem = "ODOS";
    public const string ProductionOpeningSourceTable = "odos_opening_data";
    public const string SyntheticOpeningSourceSystem = "SYNTHETIC_ASSIGNMENT";
    public const string SyntheticOpeningSourceTable = "synthetic_odos_opening";

    public static IReadOnlyList<LedgerPosition> Project(
        DealerUrn dealerUrn,
        OdosOpeningSnapshot? opening,
        IEnumerable<LedgerAdjustmentFact>? adjustments,
        bool openingIsSynthetic = false)
    {
        var lines = new List<LedgerPosition>();

        if (opening is not null)
        {
            var sourceSystem = openingIsSynthetic ? SyntheticOpeningSourceSystem : ProductionOpeningSourceSystem;
            var sourceTable = openingIsSynthetic ? SyntheticOpeningSourceTable : ProductionOpeningSourceTable;
            var sourceKey =
                $"{opening.Company}|{opening.Year}|{opening.Month}|{opening.DepotCode}|{opening.DealerCode}|{opening.TrxId}|{opening.DocNo}";

            // Gross AR from Oracle-supplied od_os_amt_updt — do not derive from od_os_amt0..4.
            lines.Add(new LedgerPosition(
                dealerUrn,
                documentType: MapDocTypeToGross(opening.DocType),
                dueDate: opening.TrxDate,
                postedOn: opening.TrxDate,
                amount: opening.OutstandingTotal,
                lineage: new LineItemRef(
                    sourceSystem,
                    sourceTable,
                    sourceKey,
                    opening.OsAmtUpdt,
                    opening.TrxDate)));
        }

        if (adjustments is not null)
        {
            foreach (var adj in adjustments)
            {
                if (!adj.DealerUrn.Equals(dealerUrn))
                    continue;

                lines.Add(new LedgerPosition(
                    adj.DealerUrn,
                    adj.DocumentType,
                    adj.DueDate,
                    adj.PostedOn,
                    adj.Amount,
                    adj.Lineage));
            }
        }

        return lines;
    }

    private static string MapDocTypeToGross(string docType)
    {
        if (string.IsNullOrWhiteSpace(docType))
            return "Invoice";
        // Preserve invoice-like classification for A1; unknown docs still map to Gross/Invoice path.
        if (docType.Contains("INV", StringComparison.OrdinalIgnoreCase)
            || docType.Contains("AR", StringComparison.OrdinalIgnoreCase)
            || docType.Contains("OS", StringComparison.OrdinalIgnoreCase))
            return "Invoice";
        return "Invoice";
    }
}

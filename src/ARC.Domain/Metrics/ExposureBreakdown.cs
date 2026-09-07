using ARC.Domain.Enums;
using ARC.Domain.Exceptions;
using ARC.Domain.ValueObjects;

namespace ARC.Domain.Metrics;

/// <summary>
/// net_recoverable_exposure(dealer, asOf) =
///   gross_open_AR − unapplied_credit_notes − accrued_scheme_rebates
///   − goods_return_in_transit − cheques_in_clearing − disputed_under_review
/// </summary>
public sealed record ExposureBreakdown
{
    public required DealerUrn DealerUrn { get; init; }
    public required DateOnly AsOf { get; init; }
    public required Money GrossOpenAr { get; init; }
    public required Money UnappliedCreditNotes { get; init; }
    public required Money AccruedSchemeRebates { get; init; }
    public required Money GoodsReturnInTransit { get; init; }
    public required Money ChequesInClearing { get; init; }
    public required Money DisputedUnderReview { get; init; }
    public required Money NetRecoverableExposure { get; init; }
    public required ReconciliationStatus Status { get; init; }
    public required IReadOnlyList<LineItemRef> Lineage { get; init; }

    public decimal UnappliedCreditRatio =>
        GrossOpenAr.Amount == 0m ? 0m : UnappliedCreditNotes.Amount / GrossOpenAr.Amount;
}

public static class MetricContract
{
    public const string Version = "net_recoverable_exposure.v1";

    public static ExposureBreakdown Compute(
        DealerUrn dealerUrn,
        DateOnly asOf,
        Money grossOpenAr,
        Money unappliedCreditNotes,
        Money accruedSchemeRebates,
        Money goodsReturnInTransit,
        Money chequesInClearing,
        Money disputedUnderReview,
        IReadOnlyList<LineItemRef> lineage,
        bool fullyReconciled)
    {
        var net = grossOpenAr
            - unappliedCreditNotes
            - accruedSchemeRebates
            - goodsReturnInTransit
            - chequesInClearing
            - disputedUnderReview;

        if (net.IsNegative)
            net = Money.Zero;

        return new ExposureBreakdown
        {
            DealerUrn = dealerUrn,
            AsOf = asOf,
            GrossOpenAr = grossOpenAr,
            UnappliedCreditNotes = unappliedCreditNotes,
            AccruedSchemeRebates = accruedSchemeRebates,
            GoodsReturnInTransit = goodsReturnInTransit,
            ChequesInClearing = chequesInClearing,
            DisputedUnderReview = disputedUnderReview,
            NetRecoverableExposure = net,
            Status = fullyReconciled ? ReconciliationStatus.Reconciled : ReconciliationStatus.Unreconciled,
            Lineage = lineage
        };
    }

    public static void EnsureReconciled(ExposureBreakdown exposure)
    {
        if (exposure.Status == ReconciliationStatus.Unreconciled)
            throw new UnreconciledPositionException(exposure.DealerUrn.Value);
    }
}

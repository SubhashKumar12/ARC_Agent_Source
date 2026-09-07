using ARC.Domain.BusinessChat;
using ARC.Domain.Metrics;
using ARC.Tools.Evidence;
using ARC.Tools.Field;
using ARC.Tools.Legal;
using ARC.Tools.Reconciliation;

namespace ARC.Api.Chat;

public static class BusinessChatFactBundleAssembler
{
    public static DealerIdentityFacts FromDealerDetail(DealerDetailChatSafeContract facts)
        => new(
            facts.DealerCode,
            facts.DealerName,
            facts.DepotCode,
            facts.DepotName,
            facts.DepotRegion,
            facts.RegionName,
            facts.TerritoryCode,
            facts.TerritoryName,
            facts.BillTo,
            facts.CustomerType,
            facts.MotherAccount,
            facts.SblCode,
            facts.GoldSilver,
            facts.PrimaryFlag);

    public static DealerFinancialFacts FromOutstanding(OutstandingDetailChatSafeContract facts)
        => new(
            facts.PeriodKey,
            facts.CurrentOutstanding,
            facts.OutstandingBucket0,
            facts.OutstandingBucket1,
            facts.OutstandingBucket2,
            facts.OutstandingBucket3,
            facts.OutstandingBucket4,
            facts.Over90Outstanding,
            facts.BusinessLineLimit);

    public static DealerRecoveryFacts FromOutstandingRecovery(OutstandingDetailChatSafeContract facts)
        => new(
            facts.NoticeGeneratedYn,
            facts.NoticeDepotYn,
            facts.NoticeHoYn,
            facts.NoticeDepotDate,
            facts.NoticeHoDate,
            facts.NoticeDepotRemarks,
            facts.NoticeHoRemarks,
            facts.RecoveryStatusCode,
            facts.RecoveryStatusDescription,
            facts.LegalStatusCode,
            facts.LegalStatusDescription);

    public static DealerFieldFacts FromField(FieldRecoveryChatFacts facts)
        => new(
            facts.PtpAmount,
            facts.PtpDate,
            facts.PtpStatus,
            facts.PtpConfidence,
            facts.ChaseStatus,
            facts.LastVisitDate,
            facts.VisitStatus,
            facts.VisitOwner,
            facts.VisitPlanStatus,
            null,
            null);

    public static DealerFieldFacts FromOutstandingVisit(OutstandingDetailChatSafeContract facts)
        => new(
            null,
            null,
            null,
            null,
            null,
            null,
            facts.TsiVisitStatus,
            null,
            null,
            facts.TsiVisitCount,
            facts.DealerFeedback);

    private static DealerFieldFacts MergeFieldFacts(DealerFieldFacts? field, DealerFieldFacts odosVisit)
        => field is null
            ? odosVisit
            : new(
                field.PtpAmount,
                field.PtpDate,
                field.PtpStatus,
                field.PtpConfidence,
                field.ChaseStatus,
                field.LastVisitDate,
                odosVisit.VisitStatus ?? field.VisitStatus,
                field.VisitOwner,
                field.VisitPlanStatus,
                odosVisit.TsiVisitCount ?? field.TsiVisitCount,
                odosVisit.DealerFeedback ?? field.DealerFeedback);

    public static DealerLegalFacts FromLegal(LegalRecoveryChatFacts facts)
        => new(
            facts.ChequeNumberMasked,
            facts.ChequeDate,
            facts.ChequeAmount,
            facts.ChequeStatus,
            facts.ReturnMemoReason,
            facts.ReturnMemoAvailability,
            facts.Section138Eligible,
            facts.EligibilityReason,
            facts.LimitationDaysRemaining,
            facts.NoticeByDate,
            facts.CureByDate,
            facts.FileByDate,
            facts.LegalDeadlineStatus,
            facts.TbcIndicators);

    public static DealerEvidenceFacts FromEvidence(EvidenceStatusChatFacts facts)
        => new(
            facts.CompletenessScore,
            facts.MissingEvidence,
            facts.LegalDocumentStatus,
            facts.CaseReference);

    public static DealerExposureFacts FromExposure(NetExposureChatFacts facts)
        => new(
            facts.GrossOpenAr,
            facts.NetRecoverableExposure,
            facts.ReconciliationStatus,
            facts.MissingComponents);

    public static BusinessChatFactBundle Merge(
        DealerDetailChatSafeContract? dealer,
        OutstandingDetailChatSafeContract? outstanding,
        FieldRecoveryChatFacts? field = null,
        LegalRecoveryChatFacts? legal = null,
        EvidenceStatusChatFacts? evidence = null,
        NetExposureChatFacts? exposure = null,
        IReadOnlyList<MetricUnavailability>? unavailable = null)
    {
        var dealerCode = dealer?.DealerCode ?? outstanding?.DealerCode ?? "";
        var depotCode = dealer?.DepotCode ?? outstanding?.DepotCode ?? "";
        var fieldFacts = field is null ? null : FromField(field);
        if (outstanding is not null && HasOdosVisitFacts(outstanding))
            fieldFacts = fieldFacts is null
                ? FromOutstandingVisit(outstanding)
                : MergeFieldFacts(fieldFacts, FromOutstandingVisit(outstanding));

        return new BusinessChatFactBundle(
            dealerCode,
            depotCode,
            dealer is null ? null : FromDealerDetail(dealer),
            outstanding is null ? null : FromOutstanding(outstanding),
            outstanding is null ? null : FromOutstandingRecovery(outstanding),
            fieldFacts,
            legal is null ? null : FromLegal(legal),
            evidence is null ? null : FromEvidence(evidence),
            exposure is null ? null : FromExposure(exposure),
            unavailable);
    }

    private static bool HasOdosVisitFacts(OutstandingDetailChatSafeContract outstanding)
        => !string.IsNullOrWhiteSpace(outstanding.TsiVisitStatus)
           || outstanding.TsiVisitCount is not null
           || !string.IsNullOrWhiteSpace(outstanding.DealerFeedback);
}

using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.Rules;
using ARC.Domain.ValueObjects;
using ARC.Tools.Exceptions;
using Microsoft.Extensions.Logging;

namespace ARC.Tools.Legal;

/// <summary>
/// Composed cheque + Section 138 + limitation read for Business Chat.
/// Reuses <see cref="LegalEligibilityTool"/> — no second legal engine.
/// </summary>
public sealed class GetLegalRecoveryDetailsTool
{
    public const string Name = "getLegalRecoveryDetails";

    private readonly IChequeRepository _cheques;
    private readonly LegalEligibilityTool _legal;
    private readonly Reconciliation.ReconciliationTool _reconciliation;
    private readonly ILogger<GetLegalRecoveryDetailsTool> _logger;

    public GetLegalRecoveryDetailsTool(
        IChequeRepository cheques,
        LegalEligibilityTool legal,
        Reconciliation.ReconciliationTool reconciliation,
        ILogger<GetLegalRecoveryDetailsTool> logger)
    {
        _cheques = cheques;
        _legal = legal;
        _reconciliation = reconciliation;
        _logger = logger;
    }

    public async Task<LegalRecoveryChatFacts> GetByDealerUrnAsync(
        string dealerUrn,
        DateOnly asOf,
        CancellationToken cancellationToken,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(dealerUrn))
            throw new ToolException(Name, "DealerUrn is required.");

        var urn = new DealerUrn(dealerUrn);
        SecurityCheque? selectedCheque = null;
        string? memoReason = null;
        string memoAvailability = "NOT_AVAILABLE";

        try
        {
            var cheques = await _cheques.ListChequesAsync(urn, cancellationToken);
            var memos = await _cheques.ListReturnMemosAsync(urn, cancellationToken);
            selectedCheque = ChequeSelection.Select(cheques, memos);
            var memo = selectedCheque is null
                ? null
                : memos.FirstOrDefault(m => string.Equals(m.ChequeNumber, selectedCheque.ChequeNumber, StringComparison.OrdinalIgnoreCase));
            if (memo is not null)
            {
                memoReason = memo.ReturnReasonCode;
                memoAvailability = "AVAILABLE";
            }
            else if (selectedCheque?.Status == ChequeStatus.Bounced)
            {
                memoAvailability = "NOT_AVAILABLE";
                memoReason = null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Tool {Tool} cheque read failed for {DealerUrn}", Name, dealerUrn);
        }

        A4ChatSafeContract legalContract;
        try
        {
            Reconciliation.ComputeNetExposureResult? exposure = null;
            try
            {
                exposure = await _reconciliation.ComputeNetExposureAsync(
                    new Reconciliation.ComputeNetExposureRequest(dealerUrn, asOf, null, correlationId),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Tool {Tool} A1 exposure unavailable for {DealerUrn}", Name, dealerUrn);
            }

            if (exposure is null)
            {
                legalContract = A4ChatSafeAssembler.FailClosed(
                    dealerUrn,
                    "Authoritative A1 exposure was not available. Section 138 eligibility was not evaluated.");
            }
            else
            {
                var result = await _legal.CheckSection138EligibilityAsync(
                    new LegalEligibilityRequest(
                        dealerUrn,
                        asOf,
                        exposure.Exposure,
                        null,
                        null,
                        correlationId),
                    cancellationToken);
                legalContract = A4ChatSafeAssembler.FromEligibility(
                    dealerUrn,
                    result.Eligibility,
                    result.Clock,
                    result.Alerts,
                    result.SelectedCheque ?? selectedCheque,
                    result.Memo,
                    demandNotice: null,
                    authoritativeExposurePresent: true);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Tool {Tool} legal evaluation failed for {DealerUrn}", Name, dealerUrn);
            legalContract = A4ChatSafeAssembler.FailClosed(
                dealerUrn,
                "Legal facts could not be loaded from deterministic sources.");
        }

        _logger.LogInformation(
            "Tool {Tool} loaded legal recovery for {DealerUrn}. CorrelationId={CorrelationId}",
            Name,
            dealerUrn,
            correlationId ?? "(none)");

        return new LegalRecoveryChatFacts(
            MaskChequeNumber(selectedCheque?.ChequeNumber ?? legalContract.SelectedChequeNumber),
            selectedCheque is null ? legalContract.MemoIssueDate : null,
            selectedCheque?.Amount.Amount,
            selectedCheque?.Status.ToString() ?? legalContract.SelectedChequeStatus,
            memoReason,
            memoAvailability,
            legalContract.Provenance.Complete ? legalContract.Eligible : null,
            legalContract.BlockReason ?? legalContract.DeterministicExplanation,
            legalContract.DaysRemaining,
            legalContract.NoticeByDate,
            legalContract.CureEndsDate,
            legalContract.FileByDate,
            legalContract.ClockStatus,
            legalContract.TbcIndicators.Select(t => t.Detail).ToList());
    }

    internal static string? MaskChequeNumber(string? chequeNumber)
    {
        if (string.IsNullOrWhiteSpace(chequeNumber))
            return null;
        var trimmed = chequeNumber.Trim();
        return trimmed.Length <= 4 ? "****" : $"****{trimmed[^4..]}";
    }
}

public sealed record LegalRecoveryChatFacts(
    string? ChequeNumberMasked,
    DateOnly? ChequeDate,
    decimal? ChequeAmount,
    string? ChequeStatus,
    string? ReturnMemoReason,
    string ReturnMemoAvailability,
    bool? Section138Eligible,
    string? EligibilityReason,
    int? LimitationDaysRemaining,
    DateOnly? NoticeByDate,
    DateOnly? CureByDate,
    DateOnly? FileByDate,
    string? LegalDeadlineStatus,
    IReadOnlyList<string> TbcIndicators);

using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.Rules;
using ARC.Domain.ValueObjects;
using ARC.Tools.Drafting;

namespace ARC.Eval.Metrics;

/// <summary>Authoritative draft amount label supplied externally — never LLM-generated.</summary>
public sealed record DraftAmountLabelRecord(
    string CaseId,
    string DealerUrn,
    decimal ExpectedDraftAmount,
    string ExpectedCurrency,
    IReadOnlyList<string> SourceEvidenceIds,
    DraftQuotedFields Draft,
    DraftKind Kind,
    RuleContext Context);

public static class DraftAmountEvalHarness
{
    public static DraftAmountEvaluation Evaluate(
        IReadOnlyList<DraftAmountLabelRecord> labels,
        DraftingVerificationTool verifyTool)
    {
        if (labels.Count == 0)
            return DraftAmountEvaluation.InsufficientLabels(noticeCases: 0);

        var compared = 0;
        var matches = 0;
        var mismatches = 0;

        foreach (var label in labels)
        {
            var ctx = label.Context;
            if (ctx.Exposure is null || ctx.Dealer is null)
                continue;

            var result = verifyTool.Verify(new DraftingVerificationRequest(
                label.Draft,
                label.Kind,
                ctx.Exposure,
                ctx.Dealer,
                ctx.Cheque,
                ctx.ReturnMemo,
                ctx.Clock,
                null,
                label.CaseId));

            var claimCheck = result.Checks.FirstOrDefault(c =>
                string.Equals(c.Field, "ClaimAmount", StringComparison.OrdinalIgnoreCase));
            if (claimCheck?.AuthoritativeValue is null)
                continue;

            compared++;
            var authoritative = decimal.Parse(claimCheck.AuthoritativeValue, System.Globalization.CultureInfo.InvariantCulture);
            if (authoritative == label.ExpectedDraftAmount
                && string.Equals(label.ExpectedCurrency, "INR", StringComparison.OrdinalIgnoreCase))
                matches++;
            else
                mismatches++;
        }

        if (compared == 0)
        {
            return new DraftAmountEvaluation(
                EvaluationMeasurementStatus.InsufficientLabels,
                labels.Count,
                0, 0, 0,
                "Labels supplied but none produced a comparable ClaimAmount check.");
        }

        return new DraftAmountEvaluation(
            EvaluationMeasurementStatus.Measured,
            labels.Count,
            compared,
            matches,
            mismatches,
            $"Compared {compared} authoritative draft amount labels.");
    }
}

public interface IEvaluationDatasetImporter<TRecord>
{
    string DatasetId { get; }
    string CorpusVersion { get; }
    CorpusLabelAuthority LabelAuthority { get; }
    IReadOnlyList<TRecord> Load();
}

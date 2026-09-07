using System.Globalization;
using ARC.Domain.Metrics;
using ARC.Domain.Odos;

namespace ARC.Api.Chat;

public static class BusinessChatNarrationBuilder
{
    private static readonly CultureInfo InrCulture = CultureInfo.GetCultureInfo("en-IN");

    public static string BuildDealerDetailAnswer(DealerDetailChatSafeContract facts)
    {
        var lines = new List<string>
        {
            $"Dealer {facts.DealerCode} — {facts.DealerName}.",
            $"Depot {facts.DepotCode} — {facts.DepotName}.",
            FormatOptional("Depot region", facts.DepotRegion),
            FormatOptional("Region name", facts.RegionName),
            FormatOptional("Territory", facts.TerritoryCode, facts.TerritoryName),
            FormatOptional("SBL", facts.SblCode),
            FormatOptional("Gold/Silver", facts.GoldSilver),
            FormatOptional("Bill To", facts.BillTo),
            FormatOptional("Primary flag", facts.PrimaryFlag),
            FormatOptional("Customer type", facts.CustomerType),
            FormatOptional("Mother account", facts.MotherAccount),
            $"Source: {facts.DataSource}."
        };
        return string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    public static string BuildOutstandingAnswer(
        OutstandingDetailChatSafeContract facts,
        OutstandingAnswerFocus focus)
    {
        var header = BuildOutstandingHeader(facts);
        return focus switch
        {
            OutstandingAnswerFocus.CurrentOnly => header,
            OutstandingAnswerFocus.Ageing => Join(header, BuildAgeingSection(facts)),
            OutstandingAnswerFocus.Over90 => Join(header, BuildOver90Line(facts)),
            OutstandingAnswerFocus.OdosStatus => Join(header, BuildOdosStatusSection(facts)),
            OutstandingAnswerFocus.NoticeStatus => Join(header, BuildNoticeSection(facts)),
            _ => Join(
                header,
                BuildAgeingSection(facts),
                BuildOver90Line(facts),
                BuildOdosStatusSection(facts),
                BuildNoticeSection(facts),
                FormatOptional("Business line limit", FormatMoney(facts.BusinessLineLimit)))
        };
    }

    public static string BuildDepotFollowUpAnswer(BusinessChatConversationContext context)
        => $"This dealer is under depot {context.DepotCode}"
           + (string.IsNullOrWhiteSpace(context.DepotName) ? "." : $" ({context.DepotName}).");

    public static string BuildDepotClarification(string dealerCode)
        => $"Please provide the depot code for dealer {dealerCode}.";

    private static string BuildOutstandingHeader(OutstandingDetailChatSafeContract facts)
    {
        var dealerLabel = string.IsNullOrWhiteSpace(facts.DealerName)
            ? facts.DealerCode
            : $"{facts.DealerCode} — {facts.DealerName}";
        var depotLabel = string.IsNullOrWhiteSpace(facts.DepotName)
            ? facts.DepotCode
            : $"{facts.DepotCode} — {facts.DepotName}";

        return Join(
            $"Outstanding for dealer {dealerLabel} in depot {depotLabel} (period {facts.PeriodKey}).",
            $"Current Outstanding: {FormatMoney(facts.CurrentOutstanding)}.");
    }

    private static string BuildAgeingSection(OutstandingDetailChatSafeContract facts)
    {
        var lines = new List<string> { "Ageing breakup:" };
        foreach (var (label, amount) in OdosOutstandingSemantics.BucketBreakdown(
                     facts.OutstandingBucket0,
                     facts.OutstandingBucket1,
                     facts.OutstandingBucket2,
                     facts.OutstandingBucket3,
                     facts.OutstandingBucket4))
        {
            lines.Add($"  {label}: {FormatMoney(amount)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildOver90Line(OutstandingDetailChatSafeContract facts)
        => $">90 Days Outstanding: {FormatMoney(facts.Over90Outstanding)}.";

    private static string BuildOdosStatusSection(OutstandingDetailChatSafeContract facts)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(facts.RecoveryStatusDescription)
            || !string.IsNullOrWhiteSpace(facts.RecoveryStatusCode))
        {
            lines.Add(FormatStatus("Recovery status", facts.RecoveryStatusCode, facts.RecoveryStatusDescription));
        }

        if (!string.IsNullOrWhiteSpace(facts.LegalStatusDescription)
            || !string.IsNullOrWhiteSpace(facts.LegalStatusCode))
        {
            lines.Add(FormatStatus("Legal status", facts.LegalStatusCode, facts.LegalStatusDescription));
        }

        if (lines.Count == 0)
            lines.Add("Recovery / ODOS status is not available for this dealer in the configured period.");

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildNoticeSection(OutstandingDetailChatSafeContract facts)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(facts.NoticeGeneratedYn))
            lines.Add($"Demand notice generated: {facts.NoticeGeneratedYn}.");

        if (!string.IsNullOrWhiteSpace(facts.NoticeHoYn) || facts.NoticeHoDate is not null)
        {
            lines.Add(FormatNoticeAction("HO notice decision", facts.NoticeHoYn, facts.NoticeHoDate, facts.NoticeHoRemarks));
        }

        if (!string.IsNullOrWhiteSpace(facts.NoticeDepotYn) || facts.NoticeDepotDate is not null)
        {
            lines.Add(FormatNoticeAction("Depot notice decision", facts.NoticeDepotYn, facts.NoticeDepotDate, facts.NoticeDepotRemarks));
        }

        if (lines.Count == 0)
            lines.Add("Notice status is not available for this dealer in the configured period.");

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatNoticeAction(
        string label,
        string? decision,
        DateOnly? date,
        string? remarks)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(decision))
            parts.Add(decision);
        if (date is not null)
            parts.Add(date.Value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(remarks))
            parts.Add(remarks);

        return parts.Count == 0
            ? $"{label}: not available."
            : $"{label}: {string.Join(" — ", parts)}.";
    }

    private static string FormatStatus(string label, string? code, string? description)
    {
        if (!string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(code))
            return $"{label}: {code} ({description}).";
        if (!string.IsNullOrWhiteSpace(description))
            return $"{label}: {description}.";
        if (!string.IsNullOrWhiteSpace(code))
            return $"{label}: {code}.";
        return $"{label}: not available.";
    }

    private static string FormatMoney(decimal? amount)
        => amount is null ? "not available" : $"₹{amount.Value.ToString("N2", InrCulture)}";

    private static string FormatOptional(string label, string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : $"{label}: {value}.";

    private static string FormatOptional(string label, string? code, string? name)
    {
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name))
            return "";
        if (string.IsNullOrWhiteSpace(name))
            return $"{label}: {code}.";
        if (string.IsNullOrWhiteSpace(code))
            return $"{label}: {name}.";
        return $"{label}: {code} ({name}).";
    }

    private static string Join(params string[] parts)
        => string.Join(Environment.NewLine, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
}

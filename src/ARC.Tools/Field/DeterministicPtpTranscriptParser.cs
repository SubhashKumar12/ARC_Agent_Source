using System.Globalization;
using System.Text.RegularExpressions;

namespace ARC.Tools.Field;

/// <summary>Schema-constrained candidate fields. Never invents missing amount or date.</summary>
public sealed record ParsedPtpCandidate(DateOnly? CommitmentDate, decimal? Amount)
{
    public bool IsComplete => CommitmentDate is not null && Amount is > 0m;
}

public interface IPtpTranscriptParser
{
    ParsedPtpCandidate Parse(string? transcript, DateOnly asOf);
}

/// <summary>
/// Local/deterministic parse for amounts and dates present in the transcript.
/// Production A6 may additionally use ChatCapability.Extraction; this parser does not call an LLM.
/// </summary>
public sealed class DeterministicPtpTranscriptParser : IPtpTranscriptParser
{
    private static readonly Regex IsoDate = new(@"\b(\d{4}-\d{2}-\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex Weekday = new(
        @"\b(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Amount = new(
        @"\b(\d{1,3}(?:,\d{2,3})+|\d+)(?:\.\d{1,2})?\b",
        RegexOptions.Compiled);

    public ParsedPtpCandidate Parse(string? transcript, DateOnly asOf)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return new ParsedPtpCandidate(null, null);

        var text = transcript.Trim();
        var date = TryIsoDate(text) ?? TryWeekday(text, asOf);
        var amountSource = IsoDate.Replace(text, " ");
        var amount = TryAmount(amountSource, date);
        return new ParsedPtpCandidate(date, amount);
    }

    private static DateOnly? TryIsoDate(string text)
    {
        var match = IsoDate.Match(text);
        if (!match.Success)
            return null;
        return DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static DateOnly? TryWeekday(string text, DateOnly asOf)
    {
        var match = Weekday.Match(text);
        if (!match.Success)
            return null;
        if (!Enum.TryParse<DayOfWeek>(match.Groups[1].Value, ignoreCase: true, out var day))
            return null;

        var delta = ((int)day - (int)asOf.DayOfWeek + 7) % 7;
        return asOf.AddDays(delta);
    }

    private static decimal? TryAmount(string text, DateOnly? parsedDate)
    {
        foreach (Match match in Amount.Matches(text))
        {
            var raw = match.Value.Replace(",", "", StringComparison.Ordinal);
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                continue;
            if (value <= 0m)
                continue;
            if (parsedDate is { } date && LooksLikeYear(value, date))
                continue;
            if (value is >= 1900m and <= 2100m && value == decimal.Truncate(value) && IsoDate.IsMatch(text))
                continue;
            return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        return null;
    }

    private static bool LooksLikeYear(decimal value, DateOnly date)
        => value == date.Year && value == decimal.Truncate(value);
}

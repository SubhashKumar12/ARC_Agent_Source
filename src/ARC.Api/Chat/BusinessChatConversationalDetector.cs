using System.Text.RegularExpressions;

namespace ARC.Api.Chat;

public enum ConversationalIntentKind
{
    Greeting = 0,
    Thanks = 1,
    Help = 2,
    Goodbye = 3,
    OutOfScope = 4,
}

public static partial class BusinessChatConversationalDetector
{
    public static ConversationalIntentKind? TryDetect(string message)
    {
        var normalized = Normalize(message);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        if (IsGreeting(normalized))
            return ConversationalIntentKind.Greeting;
        if (IsThanks(normalized))
            return ConversationalIntentKind.Thanks;
        if (IsHelp(normalized))
            return ConversationalIntentKind.Help;
        if (IsGoodbye(normalized))
            return ConversationalIntentKind.Goodbye;
        if (IsOutOfScope(normalized))
            return ConversationalIntentKind.OutOfScope;

        return null;
    }

    public static string ComposeResponse(ConversationalIntentKind kind)
    {
        return kind switch
        {
            ConversationalIntentKind.Greeting => ComposeGreeting(),
            ConversationalIntentKind.Thanks =>
                "You're welcome. You can continue with the current dealer or ask about another recovery case.",
            ConversationalIntentKind.Help => ComposeHelp(),
            ConversationalIntentKind.Goodbye =>
                "Thank you for using ARC Recovery Assistant. You can return anytime to continue recovery work.",
            ConversationalIntentKind.OutOfScope => ComposeOutOfScope(),
            _ => ComposeOutOfScope(),
        };
    }

    private static string ComposeGreeting()
    {
        var salutation = LocalSalutation();
        return $"""
{salutation}! 👋 Welcome to ARC Recovery Assistant.
I can help you with dealer outstanding, ageing, recovery status, notices, field activity, PTP, cheque and evidence information.

You can start with:
'Show dealer 115014 in depot 020.'
""";
    }

    private static string ComposeHelp()
        => """
I can help with:
• Dealer overview
• Current outstanding
• Ageing and >90 outstanding
• Business line limit
• Notice and recovery status
• TSI visit information
• PTP information
• Cheque/legal information when authoritative data is available
• Evidence status

Financial and legal conclusions are returned only when supported by approved authoritative data.
""";

    private static string ComposeOutOfScope()
        => """
I'm focused on receivables recovery and Section 138 support.
I can help with dealer, outstanding, ageing, notice, recovery, field activity, PTP, cheque and evidence-related questions.
""";

    private static string LocalSalutation()
    {
        var hour = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.Local).Hour;
        if (hour < 12) return "Good morning";
        if (hour < 18) return "Good afternoon";
        return "Good evening";
    }

    private static string Normalize(string message)
    {
        var text = message.Trim().ToLowerInvariant();
        text = PunctuationTrim().Replace(text, "");
        text = WhitespaceCollapse().Replace(text, " ");
        return text;
    }

    private static bool IsGreeting(string normalized)
    {
        if (GreetingExact().IsMatch(normalized))
            return true;
        return GreetingPhrase().IsMatch(normalized);
    }

    private static bool IsThanks(string normalized)
        => ThanksPattern().IsMatch(normalized);

    private static bool IsHelp(string normalized)
        => HelpPattern().IsMatch(normalized);

    private static bool IsGoodbye(string normalized)
        => GoodbyePattern().IsMatch(normalized);

    private static bool IsOutOfScope(string normalized)
    {
        if (OutOfScopePattern().IsMatch(normalized))
            return true;

        if (normalized.Length > 120)
            return true;

        if (normalized.Contains("who is the") || normalized.Contains("who was the"))
            return true;

        return false;
    }

    [GeneratedRegex(@"^h{1}i{1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex GreetingExact();

    [GeneratedRegex(@"^(hello|hey|good morning|good afternoon|good evening)(\s+there)?$", RegexOptions.CultureInvariant)]
    private static partial Regex GreetingPhrase();

    [GeneratedRegex(@"^(thanks|thank you|thanks a lot|okay thanks|ok thanks|thankyou)(\s+.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ThanksPattern();

    [GeneratedRegex(@"^(help|what can you do|how can you help|what can i ask|show me what you can do)(\?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex HelpPattern();

    [GeneratedRegex(@"^(bye|goodbye|good bye|see you|see ya)(\s+.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex GoodbyePattern();

    [GeneratedRegex(@"\b(joke|poem|story|prime minister|president|weather|football|movie|song|recipe)\b", RegexOptions.CultureInvariant)]
    private static partial Regex OutOfScopePattern();

    [GeneratedRegex(@"[^\w\s>]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PunctuationTrim();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceCollapse();
}

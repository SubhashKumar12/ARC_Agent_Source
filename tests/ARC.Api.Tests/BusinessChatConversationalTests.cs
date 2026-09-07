using ARC.Api.Chat;

namespace ARC.Api.Tests;

public sealed class BusinessChatConversationalTests
{
    [Theory]
    [InlineData("Hi")]
    [InlineData("hii")]
    [InlineData("Hiii")]
    [InlineData("Hello")]
    public void Greeting_messages_are_detected(string message)
    {
        Assert.Equal(ConversationalIntentKind.Greeting, BusinessChatConversationalDetector.TryDetect(message));
        var response = BusinessChatConversationalDetector.ComposeResponse(ConversationalIntentKind.Greeting);
        Assert.Contains("Welcome to ARC Recovery Assistant", response, StringComparison.Ordinal);
        Assert.DoesNotContain("cannot currently be confirmed", response, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Thanks")]
    [InlineData("thank you")]
    [InlineData("ok thanks")]
    public void Thanks_messages_are_detected(string message)
    {
        Assert.Equal(ConversationalIntentKind.Thanks, BusinessChatConversationalDetector.TryDetect(message));
        var response = BusinessChatConversationalDetector.ComposeResponse(ConversationalIntentKind.Thanks);
        Assert.Contains("welcome", response, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Help")]
    [InlineData("What can you do?")]
    [InlineData("how can you help")]
    public void Help_messages_are_detected(string message)
    {
        Assert.Equal(ConversationalIntentKind.Help, BusinessChatConversationalDetector.TryDetect(message));
        var response = BusinessChatConversationalDetector.ComposeResponse(ConversationalIntentKind.Help);
        Assert.Contains("Dealer overview", response, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Current outstanding", response, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Bye")]
    [InlineData("goodbye")]
    [InlineData("see you")]
    public void Goodbye_messages_are_detected(string message)
    {
        Assert.Equal(ConversationalIntentKind.Goodbye, BusinessChatConversationalDetector.TryDetect(message));
    }

    [Theory]
    [InlineData("Tell me a joke")]
    [InlineData("Who is the prime minister?")]
    public void Out_of_scope_messages_are_detected(string message)
    {
        Assert.Equal(ConversationalIntentKind.OutOfScope, BusinessChatConversationalDetector.TryDetect(message));
        var response = BusinessChatConversationalDetector.ComposeResponse(ConversationalIntentKind.OutOfScope);
        Assert.Contains("receivables recovery", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Business_dealer_query_is_not_conversational()
    {
        Assert.Null(BusinessChatConversationalDetector.TryDetect("Show dealer 115014 in depot 020"));
    }
}

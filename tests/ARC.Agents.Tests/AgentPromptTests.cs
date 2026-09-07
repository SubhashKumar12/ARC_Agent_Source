using ARC.Agents.Prompts;

namespace ARC.Agents.Tests;

public sealed class AgentPromptTests
{
    public static TheoryData<string, string> Prompts => new()
    {
        { "A1", AgentPrompts.A1 },
        { "A2", AgentPrompts.A2 },
        { "A3", AgentPrompts.A3 },
        { "A4", AgentPrompts.A4 },
        { "A5", AgentPrompts.A5 },
        { "A6", AgentPrompts.A6 },
        { "A7", AgentPrompts.A7 },
        { "A8", AgentPrompts.A8 }
    };

    [Theory]
    [MemberData(nameof(Prompts))]
    public void Prompt_forbids_human_gate_approval(string id, string prompt)
    {
        Assert.Contains("must not approve", prompt, StringComparison.OrdinalIgnoreCase);
        _ = id;
    }

    [Fact]
    public void A3_has_no_submit_notice_tool()
    {
        Assert.Contains("There is no SubmitNotice tool", AgentPrompts.A3, StringComparison.Ordinal);
        Assert.Contains("You must not despatch a notice", AgentPrompts.A3, StringComparison.Ordinal);
        string[] prompts = [AgentPrompts.A1, AgentPrompts.A2, AgentPrompts.A3, AgentPrompts.A4, AgentPrompts.A5, AgentPrompts.A6, AgentPrompts.A7, AgentPrompts.A8];
        foreach (var prompt in prompts)
            Assert.DoesNotContain("call SubmitNotice", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Amounts_dates_and_eligibility_stay_on_tools()
    {
        Assert.Contains("ComputeNetExposure", AgentPrompts.A1);
        Assert.Contains("never calculate", AgentPrompts.A1, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DecideNotice is the only source", AgentPrompts.A3);
        Assert.Contains("must not calculate notice windows", AgentPrompts.A4, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must not remove TBC", AgentPrompts.A4, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must not change eligibility", AgentPrompts.A4, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("You must not confirm a Promise-to-Pay", AgentPrompts.A6);
        Assert.Contains("You must not approve legal case-file review gate G4", AgentPrompts.A7);
        Assert.Contains("must not remove TBC", AgentPrompts.A8, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must not invent a Broken PTP", AgentPrompts.A8, StringComparison.OrdinalIgnoreCase);
    }
}

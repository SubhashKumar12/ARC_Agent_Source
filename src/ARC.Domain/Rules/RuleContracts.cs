using ARC.Domain.Enums;
using ARC.Domain.Exceptions;

namespace ARC.Domain.Rules;

public sealed record RuleResult(
    string RuleId,
    string Version,
    bool Passed,
    bool IsBlocking,
    string Message)
{
    public bool Blocks => IsBlocking && !Passed;
}

public interface IRule
{
    string Id { get; }
    string Version { get; }
    RuleSet Set { get; }
    bool IsBlocking { get; }
    RuleResult Evaluate(RuleContext context);
}

public static class RuleGuard
{
    public static T Require<T>(T? value, string ruleId, string prerequisite) where T : class
    {
        if (value is null)
            throw new MissingRulePrerequisiteException(ruleId, prerequisite);
        return value;
    }
}

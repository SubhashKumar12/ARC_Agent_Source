using ARC.Domain.Enums;
using ARC.Domain.Exceptions;

namespace ARC.Domain.Rules;

/// <summary>R5 — Block all recovery action against a dealer under insolvency moratorium.</summary>
public sealed class R5MoratoriumRule : IRule
{
    public string Id => "R5";
    public string Version { get; }
    public RuleSet Set => RuleSet.AllActions;
    public bool IsBlocking => true;

    public R5MoratoriumRule(string version) => Version = version;

    public RuleResult Evaluate(RuleContext context)
    {
        var dealer = RuleGuard.Require(context.Dealer, Id, "Dealer");
        var passed = !dealer.UnderInsolvencyMoratorium;
        return new RuleResult(
            Id,
            Version,
            passed,
            IsBlocking,
            passed ? "Dealer is not under moratorium." : "Dealer is under insolvency moratorium — block every action.");
    }
}

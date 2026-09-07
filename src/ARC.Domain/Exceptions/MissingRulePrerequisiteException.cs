namespace ARC.Domain.Exceptions;

/// <summary>Thrown when a rule cannot evaluate because required input is missing (fail-closed).</summary>
public sealed class MissingRulePrerequisiteException : DomainException
{
    public string RuleId { get; }
    public string Prerequisite { get; }

    public MissingRulePrerequisiteException(string ruleId, string prerequisite)
        : base($"Rule {ruleId} cannot evaluate: missing {prerequisite}. Fail-closed — never default to Pass.")
    {
        RuleId = ruleId;
        Prerequisite = prerequisite;
    }
}

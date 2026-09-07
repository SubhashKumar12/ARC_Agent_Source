namespace ARC.Agents.Observability;

/// <summary>Assignment span names and tags. Never put prompt, transcript, or PII on activities.</summary>
public static class ArcTelemetry
{
    public const string SourceName = "ARC";
    public const string Cycle = "arc.cycle";
    public const string Dealer = "arc.dealer";
    public const string Executor = "arc.executor";
    public const string Gate = "arc.gate";
    public const string LlmExplain = "arc.llm.explain";
    public const string LlmExtract = "arc.llm.extract";

    public static readonly System.Diagnostics.ActivitySource Source = new(SourceName);

    public static System.Diagnostics.Activity? StartCycle(string? cycleId)
    {
        var activity = Source.StartActivity(Cycle);
        Set(activity, "cycle_id", cycleId);
        return activity;
    }

    public static System.Diagnostics.Activity? StartDealer(string? dealerUrn)
    {
        var activity = Source.StartActivity(Dealer);
        Set(activity, "dealer_urn", dealerUrn);
        return activity;
    }

    public static System.Diagnostics.Activity? StartExecutor(string node)
    {
        var activity = Source.StartActivity(Executor);
        Set(activity, "executor", node);
        return activity;
    }

    public static System.Diagnostics.Activity? StartGate(string gate)
    {
        var activity = Source.StartActivity(Gate);
        Set(activity, "gate_id", gate);
        return activity;
    }

    public static System.Diagnostics.Activity? StartLlm(string spanName)
        => Source.StartActivity(spanName);

    public static void Set(System.Diagnostics.Activity? activity, string name, string? value)
    {
        if (activity is null || string.IsNullOrWhiteSpace(value))
            return;
        activity.SetTag(name, value);
    }
}

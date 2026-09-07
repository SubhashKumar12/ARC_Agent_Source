namespace ARC.Tools.Exceptions;

public class ToolException : Exception
{
    public string ToolName { get; }

    public ToolException(string toolName, string message, Exception? inner = null) : base(message, inner)
        => ToolName = toolName;
}

public sealed class DealerNotFoundException : ToolException
{
    public DealerNotFoundException(string dealerUrn)
        : base("ReconciliationTool", $"Dealer '{dealerUrn}' was not found.")
    {
    }
}

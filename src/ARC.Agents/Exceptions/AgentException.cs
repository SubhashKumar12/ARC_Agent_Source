namespace ARC.Agents.Exceptions;

public sealed class AgentException : Exception
{
    public string AgentName { get; }

    public AgentException(string agentName, string message, Exception? inner = null)
        : base(message, inner)
        => AgentName = agentName;
}

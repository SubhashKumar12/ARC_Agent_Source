using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using ARC.Agents.Ai;
using ARC.Agents.Exceptions;
using ARC.Tools.Exceptions;

namespace ARC.Agents.Common;

internal static class AgentNarration
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Serialize(object value) => JsonSerializer.Serialize(value, Json);

    public static async Task<string?> ExplainAsync(
        AIAgent agent,
        string agentName,
        object authoritativeFacts,
        ILogger logger,
        CancellationToken cancellationToken,
        ArcAiOptions? ai = null)
    {
        if (ai is { NarrationEnabled: false })
            return null;

        try
        {
            var payload = Serialize(authoritativeFacts);
            var response = await agent.RunAsync(
                "AUTHORITATIVE TOOL OUTPUT follows. Do not change amounts, dates, risk scores, eligibility, or notice decisions. "
                + "Explain the result for PaintCo operations. If your wording disagrees with a tool value, the tool value remains correct.\n\n"
                + payload,
                session: null,
                options: null,
                cancellationToken: cancellationToken);
            return response.Text;
        }
        catch (Exception ex) when (ex is not ToolException and not AgentException)
        {
            logger.LogWarning(ex, "Agent {Agent} explanation failed; returning tool result without narration.", agentName);
            return null;
        }
    }
}

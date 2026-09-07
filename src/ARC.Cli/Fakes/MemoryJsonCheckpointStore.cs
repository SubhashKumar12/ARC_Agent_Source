using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;

namespace ARC.Cli.Fakes;

/// <summary>In-memory MAF JSON checkpoints for the local Shadow demo. Production Host uses Cosmos.</summary>
internal sealed class MemoryJsonCheckpointStore : ICheckpointStore<JsonElement>
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<Entry>> _sessions = new(StringComparer.Ordinal);

    public ValueTask<CheckpointInfo> CreateCheckpointAsync(string sessionId, JsonElement value, CheckpointInfo? parent)
    {
        var checkpointId = Guid.NewGuid().ToString("N");
        var info = new CheckpointInfo(sessionId, checkpointId);
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var list))
            {
                list = [];
                _sessions[sessionId] = list;
            }

            list.Add(new Entry(info, value.GetRawText(), parent?.CheckpointId));
        }

        return ValueTask.FromResult(info);
    }

    public ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var list))
                throw new InvalidOperationException($"MAF checkpoint session '{sessionId}' was not found.");

            var entry = list.FirstOrDefault(e => e.Info.CheckpointId == key.CheckpointId)
                ?? throw new InvalidOperationException($"MAF checkpoint '{key.CheckpointId}' was not found for session '{sessionId}'.");

            using var json = JsonDocument.Parse(entry.PayloadJson);
            return ValueTask.FromResult(json.RootElement.Clone());
        }
    }

    public ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(string sessionId, CheckpointInfo? withParent)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var list))
                return ValueTask.FromResult<IEnumerable<CheckpointInfo>>([]);

            IEnumerable<Entry> filtered = list;
            if (withParent is not null)
                filtered = list.Where(e => e.ParentCheckpointId == withParent.CheckpointId);

            return ValueTask.FromResult<IEnumerable<CheckpointInfo>>(filtered.Select(e => e.Info).ToList());
        }
    }

    public void Clear()
    {
        lock (_gate)
            _sessions.Clear();
    }

    private sealed record Entry(CheckpointInfo Info, string PayloadJson, string? ParentCheckpointId);
}

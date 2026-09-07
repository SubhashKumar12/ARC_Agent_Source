using ARC.Data.Blob;
using ARC.Data.Cosmos;
using ARC.Data.Messaging;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;

namespace ARC.Agents.Tests.Fakes;

internal sealed class InMemoryHarness :
    IDealerRepository,
    ILedgerRepository,
    IChequeRepository,
    IGateDecisionRepository,
    ILegalCaseRepository,
    IRecoveryCaseRepository,
    IWorkflowStateRepository,
    IConversationStateRepository,
    IAuditRepository,
    IEvidenceDocumentRepository,
    IServiceBusPublisher
{
    private readonly Dictionary<string, Dealer> _dealers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<LedgerPosition>> _ledger = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SecurityCheque>> _cheques = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ChequeReturnMemo>> _memos = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<GateDecision>> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LegalCase> _legal = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecoveryCaseIndex> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (WorkflowCheckpoint Checkpoint, RecoveryState State)> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecoveryState> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _conversation = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _evidence = new(StringComparer.Ordinal);

    public void SeedDealer(Dealer dealer) => _dealers[dealer.Urn.Value] = dealer;

    public void SeedLedger(params LedgerPosition[] lines)
    {
        foreach (var line in lines)
        {
            if (!_ledger.TryGetValue(line.DealerUrn.Value, out var list))
            {
                list = [];
                _ledger[line.DealerUrn.Value] = list;
            }

            list.Add(line);
        }
    }

    public void SeedCheque(SecurityCheque cheque)
    {
        if (!_cheques.TryGetValue(cheque.DealerUrn.Value, out var list))
        {
            list = [];
            _cheques[cheque.DealerUrn.Value] = list;
        }

        list.Add(cheque);
    }

    public void SeedMemo(ChequeReturnMemo memo)
    {
        if (!_memos.TryGetValue(memo.DealerUrn.Value, out var list))
        {
            list = [];
            _memos[memo.DealerUrn.Value] = list;
        }

        list.Add(memo);
    }

    public void SeedEvidence(string location) => _evidence.Add(location);

    public Task<Dealer?> GetAsync(DealerUrn urn, CancellationToken cancellationToken)
        => Task.FromResult(_dealers.GetValueOrDefault(urn.Value));

    public Task<IReadOnlyList<Dealer>> ListByRegionAsync(string region, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Dealer>>(_dealers.Values.Where(d => d.Region == region).ToList());

    public Task<IReadOnlyList<Dealer>> ListAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Dealer>>([.. _dealers.Values]);

    public Task<IReadOnlyList<LedgerPosition>> ListByDealerAsync(DealerUrn urn, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LedgerPosition>>(_ledger.GetValueOrDefault(urn.Value) ?? []);

    public Task<IReadOnlyList<SecurityCheque>> ListChequesAsync(DealerUrn urn, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SecurityCheque>>(_cheques.GetValueOrDefault(urn.Value) ?? []);

    public Task<IReadOnlyList<ChequeReturnMemo>> ListReturnMemosAsync(DealerUrn urn, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ChequeReturnMemo>>(_memos.GetValueOrDefault(urn.Value) ?? []);

    public Task SaveAsync(CycleId cycleId, DealerUrn dealerUrn, GateDecision decision, CancellationToken cancellationToken)
    {
        var key = $"{cycleId.Value}|{dealerUrn.Value}";
        if (!_gates.TryGetValue(key, out var list))
        {
            list = [];
            _gates[key] = list;
        }

        list.Add(decision);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GateDecision>> ListAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<GateDecision>>(_gates.GetValueOrDefault($"{cycleId.Value}|{dealerUrn.Value}") ?? []);

    Task<LegalCase?> ILegalCaseRepository.GetAsync(DealerUrn urn, CancellationToken cancellationToken)
        => Task.FromResult(_legal.GetValueOrDefault(urn.Value));

    public Task UpsertAsync(LegalCase legalCase, CancellationToken cancellationToken)
    {
        _legal[legalCase.DealerUrn.Value] = legalCase;
        return Task.CompletedTask;
    }

    public Task UpsertIndexAsync(RecoveryCaseIndex index, CancellationToken cancellationToken)
    {
        _index[$"{index.CycleId.Value}|{index.DealerUrn.Value}"] = index;
        return Task.CompletedTask;
    }

    Task<RecoveryCaseIndex?> IRecoveryCaseRepository.GetAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
        => Task.FromResult(_index.GetValueOrDefault($"{cycleId.Value}|{dealerUrn.Value}"));

    public Task<IReadOnlyList<RecoveryCaseIndex>> ListByCycleAsync(CycleId cycleId, string? region, string? depot, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<RecoveryCaseIndex>>(_index.Values.Where(i => i.CycleId.Value == cycleId.Value).ToList());

    public Task<RankedWorklist> ListRankedWorklistAsync(
        CycleId cycleId, string? region, string? depot, bool topDecile, CancellationToken cancellationToken)
        => Task.FromResult(RecoverabilityWorklistQuery.FromIndex(cycleId, topDecile, _index.Values, _dealers, region, depot));

    public Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint, RecoveryState state, CancellationToken cancellationToken)
    {
        _nodes[checkpoint.IdempotencyKey] = (checkpoint, state);
        return Task.CompletedTask;
    }

    public Task<(WorkflowCheckpoint Checkpoint, RecoveryState State)?> LoadCheckpointAsync(
        CycleId cycleId, DealerUrn dealerUrn, string node, CancellationToken cancellationToken)
        => Task.FromResult<(WorkflowCheckpoint Checkpoint, RecoveryState State)?>(
            _nodes.TryGetValue($"{cycleId.Value}|{dealerUrn.Value}|{node}", out var value) ? value : null);

    public Task<RecoveryState?> LoadLatestStateAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
        => Task.FromResult(_latest.GetValueOrDefault($"{cycleId.Value}|{dealerUrn.Value}"));

    public Task SaveStateAsync(RecoveryState state, CancellationToken cancellationToken)
    {
        _latest[$"{state.CycleId.Value}|{state.DealerUrn.Value}"] = state;
        return Task.CompletedTask;
    }

    public Task SaveAsync(CycleId cycleId, DealerUrn dealerUrn, string payloadJson, CancellationToken cancellationToken)
    {
        _conversation[$"{cycleId.Value}|{dealerUrn.Value}"] = payloadJson;
        return Task.CompletedTask;
    }

    Task<string?> IConversationStateRepository.GetAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
        => Task.FromResult(_conversation.GetValueOrDefault($"{cycleId.Value}|{dealerUrn.Value}"));

    public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<EvidenceDocument> UploadAsync(
        DealerUrn dealerUrn, DocumentType type, Stream content, string fileName, string contentType, CancellationToken cancellationToken)
    {
        var location = $"evidence/{dealerUrn.Value}/{type}/{fileName}";
        _evidence.Add(location);
        return Task.FromResult(new EvidenceDocument(dealerUrn, type, location));
    }

    public Task<Stream> DownloadAsync(EvidenceDocument document, CancellationToken cancellationToken)
        => Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(document.Location)));

    public Task<bool> ExistsAsync(EvidenceDocument document, CancellationToken cancellationToken)
        => Task.FromResult(_evidence.Contains(document.Location));

    public Task DeleteAsync(EvidenceDocument document, CancellationToken cancellationToken)
    {
        _evidence.Remove(document.Location);
        return Task.CompletedTask;
    }

    public Task PublishCycleFanOutAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task PublishAlertAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task PublishGateNotificationAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task PublishGateResumeAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

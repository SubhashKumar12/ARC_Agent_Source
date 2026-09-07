using ARC.Data.Blob;
using ARC.Data.Cosmos;
using ARC.Data.Messaging;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Domain.Workflow;

namespace ARC.Web.Services;

/// <summary>Local Shadow demo store. Not a production persistence layer.</summary>
public sealed class InMemoryArcStore :
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
    private readonly object _gate = new();
    private readonly Dictionary<string, Dealer> _dealers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<LedgerPosition>> _ledger = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SecurityCheque>> _cheques = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ChequeReturnMemo>> _memos = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<GateDecision>> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LegalCase> _legalCases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecoveryCaseIndex> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (WorkflowCheckpoint Checkpoint, RecoveryState State)> _nodeCheckpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecoveryState> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _conversations = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AuditEvent> _audit = [];
    private readonly HashSet<string> _evidence = new(StringComparer.Ordinal);
    private readonly List<BusMessage> _bus = [];

    public IReadOnlyList<BusMessage> BusMessages
    {
        get
        {
            lock (_gate)
                return [.. _bus];
        }
    }

    public IReadOnlyList<AuditEvent> Audit
    {
        get
        {
            lock (_gate)
                return [.. _audit];
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _dealers.Clear();
            _ledger.Clear();
            _cheques.Clear();
            _memos.Clear();
            _gates.Clear();
            _legalCases.Clear();
            _index.Clear();
            _nodeCheckpoints.Clear();
            _latest.Clear();
            _conversations.Clear();
            _audit.Clear();
            _evidence.Clear();
            _bus.Clear();
        }
    }

    public void SeedDealer(Dealer dealer)
    {
        lock (_gate)
            _dealers[dealer.Urn.Value] = dealer;
    }

    public void SeedLedger(params LedgerPosition[] lines)
    {
        lock (_gate)
        {
            foreach (var line in lines)
            {
                var key = line.DealerUrn.Value;
                if (!_ledger.TryGetValue(key, out var list))
                {
                    list = [];
                    _ledger[key] = list;
                }

                list.Add(line);
            }
        }
    }

    public void SeedCheque(SecurityCheque cheque)
    {
        lock (_gate)
        {
            var key = cheque.DealerUrn.Value;
            if (!_cheques.TryGetValue(key, out var list))
            {
                list = [];
                _cheques[key] = list;
            }

            list.Add(cheque);
        }
    }

    public void SeedMemo(ChequeReturnMemo memo)
    {
        lock (_gate)
        {
            var key = memo.DealerUrn.Value;
            if (!_memos.TryGetValue(key, out var list))
            {
                list = [];
                _memos[key] = list;
            }

            list.Add(memo);
        }
    }

    public void SeedEvidence(string location)
    {
        lock (_gate)
            _evidence.Add(location);
    }

    public LegalCase? PeekLegalCase(DealerUrn urn)
    {
        lock (_gate)
            return _legalCases.GetValueOrDefault(urn.Value);
    }

    public Task<Dealer?> GetAsync(DealerUrn urn, CancellationToken cancellationToken)
    {
        lock (_gate)
            return Task.FromResult(_dealers.GetValueOrDefault(urn.Value));
    }

    public Task<IReadOnlyList<Dealer>> ListByRegionAsync(string region, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<Dealer> rows = _dealers.Values
                .Where(d => string.Equals(d.Region, region, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<IReadOnlyList<Dealer>> ListAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<Dealer>>([.. _dealers.Values]);
    }

    public Task<IReadOnlyList<LedgerPosition>> ListByDealerAsync(DealerUrn urn, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<LedgerPosition> rows = _ledger.TryGetValue(urn.Value, out var list) ? [.. list] : [];
            return Task.FromResult(rows);
        }
    }

    public Task<IReadOnlyList<SecurityCheque>> ListChequesAsync(DealerUrn urn, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<SecurityCheque> rows = string.IsNullOrWhiteSpace(urn.Value)
                ? []
                : _cheques.TryGetValue(urn.Value, out var list) ? [.. list] : [];
            return Task.FromResult(rows);
        }
    }

    public Task<IReadOnlyList<ChequeReturnMemo>> ListReturnMemosAsync(DealerUrn urn, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<ChequeReturnMemo> rows = _memos.TryGetValue(urn.Value, out var list) ? [.. list] : [];
            return Task.FromResult(rows);
        }
    }

    public Task SaveAsync(CycleId cycleId, DealerUrn dealerUrn, GateDecision decision, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var key = CaseKey(cycleId, dealerUrn);
            if (!_gates.TryGetValue(key, out var list))
            {
                list = [];
                _gates[key] = list;
            }

            list.Add(decision);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GateDecision>> ListAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<GateDecision> rows = _gates.TryGetValue(CaseKey(cycleId, dealerUrn), out var list) ? [.. list] : [];
            return Task.FromResult(rows);
        }
    }

    Task<LegalCase?> ILegalCaseRepository.GetAsync(DealerUrn urn, CancellationToken cancellationToken)
        => Task.FromResult(PeekLegalCase(urn));

    public Task UpsertAsync(LegalCase legalCase, CancellationToken cancellationToken)
    {
        lock (_gate)
            _legalCases[legalCase.DealerUrn.Value] = legalCase;
        return Task.CompletedTask;
    }

    public Task UpsertIndexAsync(RecoveryCaseIndex index, CancellationToken cancellationToken)
    {
        lock (_gate)
            _index[CaseKey(index.CycleId, index.DealerUrn)] = index;
        return Task.CompletedTask;
    }

    Task<RecoveryCaseIndex?> IRecoveryCaseRepository.GetAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
    {
        lock (_gate)
            return Task.FromResult(_index.GetValueOrDefault(CaseKey(cycleId, dealerUrn)));
    }

    public Task<IReadOnlyList<RecoveryCaseIndex>> ListByCycleAsync(CycleId cycleId, string? region, string? depot, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<RecoveryCaseIndex> rows = _index.Values
                .Where(i => i.CycleId.Value == cycleId.Value)
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<RankedWorklist> ListRankedWorklistAsync(
        CycleId cycleId, string? region, string? depot, bool topDecile, CancellationToken cancellationToken)
    {
        lock (_gate)
            return Task.FromResult(RecoverabilityWorklistQuery.FromIndex(cycleId, topDecile, _index.Values, _dealers, region, depot));
    }

    public Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint, RecoveryState state, CancellationToken cancellationToken)
    {
        lock (_gate)
            _nodeCheckpoints[checkpoint.IdempotencyKey] = (checkpoint, state);
        return Task.CompletedTask;
    }

    public Task<(WorkflowCheckpoint Checkpoint, RecoveryState State)?> LoadCheckpointAsync(
        CycleId cycleId, DealerUrn dealerUrn, string node, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var key = $"{cycleId.Value}|{dealerUrn.Value}|{node}";
            return Task.FromResult<(WorkflowCheckpoint Checkpoint, RecoveryState State)?>(
                _nodeCheckpoints.TryGetValue(key, out var value) ? value : null);
        }
    }

    public Task<RecoveryState?> LoadLatestStateAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
    {
        lock (_gate)
            return Task.FromResult(_latest.GetValueOrDefault(CaseKey(cycleId, dealerUrn)));
    }

    public Task SaveStateAsync(RecoveryState state, CancellationToken cancellationToken)
    {
        lock (_gate)
            _latest[CaseKey(state.CycleId, state.DealerUrn)] = state;
        return Task.CompletedTask;
    }

    public Task SaveAsync(CycleId cycleId, DealerUrn dealerUrn, string payloadJson, CancellationToken cancellationToken)
    {
        lock (_gate)
            _conversations[CaseKey(cycleId, dealerUrn)] = payloadJson;
        return Task.CompletedTask;
    }

    Task<string?> IConversationStateRepository.GetAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
    {
        lock (_gate)
            return Task.FromResult(_conversations.GetValueOrDefault(CaseKey(cycleId, dealerUrn)));
    }

    public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        lock (_gate)
            _audit.Add(auditEvent);
        return Task.CompletedTask;
    }

    public Task<EvidenceDocument> UploadAsync(
        DealerUrn dealerUrn,
        DocumentType type,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        var location = $"evidence/{dealerUrn.Value}/{type}/{fileName}";
        lock (_gate)
            _evidence.Add(location);
        return Task.FromResult(new EvidenceDocument(dealerUrn, type, location));
    }

    public Task<Stream> DownloadAsync(EvidenceDocument document, CancellationToken cancellationToken)
        => Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(document.Location)));

    public Task<bool> ExistsAsync(EvidenceDocument document, CancellationToken cancellationToken)
    {
        if (document.Location.Contains("://", StringComparison.Ordinal))
            throw new ArgumentException("Evidence location must be a configured blob path, not an arbitrary URL.");

        lock (_gate)
            return Task.FromResult(_evidence.Contains(document.Location));
    }

    public Task DeleteAsync(EvidenceDocument document, CancellationToken cancellationToken)
    {
        lock (_gate)
            _evidence.Remove(document.Location);
        return Task.CompletedTask;
    }

    public Task PublishCycleFanOutAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => Record("cycle-fanout", messageBody, sessionOrDedupId);

    public Task PublishAlertAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => Record("alert", messageBody, sessionOrDedupId);

    public Task PublishGateNotificationAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => Record("gate-notification", messageBody, sessionOrDedupId);

    public Task PublishGateResumeAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => Record("gate-resume", messageBody, sessionOrDedupId);

    private Task Record(string queue, string body, string? id)
    {
        lock (_gate)
            _bus.Add(new BusMessage(queue, body, id));
        return Task.CompletedTask;
    }

    private static string CaseKey(CycleId cycleId, DealerUrn dealerUrn) => $"{cycleId.Value}|{dealerUrn.Value}";
}

public sealed record BusMessage(string Queue, string Body, string? Id);

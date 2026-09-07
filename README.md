# ARC — Agentic Receivables Recovery and Section 138 Legal Action Pipeline

ARC helps PaintCo recover overdue dealer receivables and progress Section 138 legal cases through governed **Microsoft Agent Framework (MAF)** workflows. Agents recommend; **tools and domain rules** own amounts, dates, and eligibility; **humans approve** at four HITL gates.

**Default mode: Shadow** — local demonstration and testing without live outbound notice, courier, court filing, or production legal submission.

| Document | Role |
|----------|------|
| [docs/SUBMISSION_INDEX.md](docs/SUBMISSION_INDEX.md) | **Evaluator start here** |
| [docs/EVALUATOR_QUICK_START.md](docs/EVALUATOR_QUICK_START.md) | Build, test, CLI commands |
| [docs/ASSIGNMENT_COMPLIANCE_MATRIX.md](docs/ASSIGNMENT_COMPLIANCE_MATRIX.md) | Requirement status |
| [docs/FINAL_VALIDATION_REPORT.md](docs/FINAL_VALIDATION_REPORT.md) | Validation evidence |
| [docs/SUBMISSION_TBC_AND_BLOCKERS.md](docs/SUBMISSION_TBC_AND_BLOCKERS.md) | Known gaps (not invented) |
| `docs/Problem_Statement_1_Receivables_Recovery_Section138 (1).pdf` | Business requirements |

**Production readiness: NO.** Submission readiness for evaluator review: **YES** (see [docs/FINAL_SUBMISSION_READINESS.md](docs/FINAL_SUBMISSION_READINESS.md)).

---

## 1. Problem statement

PaintCo operates a large dealer network. When receivables go overdue, operations rely on ODOS demand notices and, after cheque bounce and non-payment, Section 138 legal escalation. Today this work is fragmented, manual, and prone to rubber-stamp approvals and missed limitation windows.

ARC automates **recommendation and orchestration**: reconcile AR, prioritise dealers, recommend notice or reconcile, check legal eligibility and clocks, verify drafts, plan field visits, and assemble evidence — always through governed tools, deterministic rules, and human gates.

---

## 2. What ARC solves

| Problem | ARC approach |
|---------|----------------|
| Simplistic ageing triggers | Ranked worklist + tiered routing (SP3 interim) |
| Timeout treated as approval | Gate expiry ≠ approval (P2) |
| Unreconciled gross AR | A1 netting + R1 rules (P3) |
| Field execution gap | A6 visits, SP7 PTP/chase (P4, partial) |
| Unguarded S138 escalation | R2 + clocks + DI + gates G3/G2/G4 (P5) |
| Fragmented context | SP2 identity + Knowledge retrieval (P6, partial) |
| No learning loop | A8 exceptions only — SP9 learning **not implemented** (P7) |

---

## 3. Architecture summary

```
                    ARC
                     |
         +-----------+-----------+
         |                       |
      ARC.Api                  ARC.Cli
    (HTTP + Chat)         (Shadow S1–S9)
         |                       |
      ARC.Web                 (no Azure)
   (minimal chat UI)              |
         +-----------+-----------+
                     |
            ARC.Host.Functions
                     |
              MAF Workflow
           OdosCycle / Section138
                     |
                  A1–A8
                     |
                 ARC.Tools
              /            \
       ARC.Knowledge      ARC.Data
              \            /
                 ARC.Domain
```

See [docs/architecture/C4.md](docs/architecture/C4.md) and [docs/architecture/MAF_WORKFLOWS.md](docs/architecture/MAF_WORKFLOWS.md).

---

## 4. Microsoft Agent Framework

- **WorkflowBuilder** graphs in `OdosCycleWorkflow` and `Section138Workflow`
- **RequestPort** for four HITL gates (G1–G4)
- Checkpoint pause/persist/resume (CLI S8; Cosmos for cross-host — Azure)
- Executors call agents/tools; repositories for persistence
- Package: `Microsoft.Agents.AI*` (net9.0)

---

## 5. Workflow A — ODOS (`OdosCycle`)

```
A1 → A2 → A3 → G1 → A5 → G2 → A6
```

Conditional: Visit tier → A6; Hold/Reconcile → terminate.  
Evidence: CLI S1, S2, S6, S7, S8.

---

## 6. Workflow B — Section 138 (`Section138`)

```
A1 → A4 → G3 → A5 → G2 → A7 → G4
```

Court filing out of scope. Evidence: CLI S3, S4, S5.

---

## 7. Four HITL gates

| Gate | Approver | Workflow |
|------|----------|----------|
| G1 Depot Manager | Depot Manager | A (after Issue) |
| G2 Advocate signature | Advocate | A + B |
| G3 Legal progression | Legal | B |
| G4 Case-file review | Legal | B |

Expiry is **not** approval. Agents cannot approve gates (R4).

---

## 8. Deterministic vs probabilistic boundary

| Deterministic (tools / rules) | Probabilistic (LLM, guarded) |
|------------------------------|------------------------------|
| Net exposure, tiers, notice verdict | Narration, extraction assist |
| S138 eligibility, limitation clocks | Semantic chat routing (optional LLM) |
| Draft verification, PTP structure | — |
| Gate routing, Shadow suppression | — |

**No LLM final authorization** for legal or financial actions.

---

## 9. SP1–SP9 overview

| SP | Capability | Status |
|----|------------|--------|
| SP1 | Net exposure + lineage | Partial TBC |
| SP2 | Dealer360 / identity | Local PASS |
| SP3 | Recoverability ranking | Partial TBC (interim = exposure) |
| SP4 | Issue / Hold / Reconcile | Local PASS |
| SP5 | S138 + DI validation | Partial TBC |
| SP6 | Statutory clocks + draft | Partial TBC |
| SP7 | Visits + voice PTP + chase | Partial TBC |
| SP8 | Evidence bundle | Partial TBC |
| SP9 | Supervision + learning | Not implemented |

Details: [docs/ASSIGNMENT_COMPLIANCE_MATRIX.md](docs/ASSIGNMENT_COMPLIANCE_MATRIX.md).

---

## 10. A1–A8 overview

| Agent | Role |
|-------|------|
| A1 Reconciliation | Net exposure via `ComputeNetExposure` |
| A2 Prioritisation | Tier/score via `PrioritiseRecovery` |
| A3 Notice decisioning | Issue / Hold / Reconcile |
| A4 Legal eligibility | S138 + limitation clock |
| A5 Draft verification | `VerifyDraft` |
| A6 Field orchestration | Visits / PTP (never confirms PTP) |
| A7 Evidence case file | `PrepareCaseFile` |
| A8 Supervisory insight | Exception queue (API; not on MAF graphs) |

---

## 11. Business Chat

Semantic layer over approved ODOS/corporate capabilities:

- Dealer identity, outstanding, ageing, >90, business line limit  
- Notice / recovery / legal status, TSI visit aggregate  
- Cheque/legal facts where authoritative; evidence status  
- **Fail-closed** for net exposure, limitation, unsupported memo facts  

API: `POST /api/chat` · UI: `ARC.Web` · Tests: 190 BusinessChat unit tests.

---

## 12. Shadow mode

| Fact | Evidence |
|------|----------|
| Default `RunMode.Shadow` | API, CLI, Web badge |
| `ShadowOutboundGate` | No live despatch |
| CLI | *"Outbound remains Shadow"* |

`Assisted` / `Live` exist; **Live outbound is not registered**.

---

## 13. Security / PII / governance

- MCC guardrails adapter (Phase 9B)  
- PII redaction on agent input  
- R4 segregation of duties  
- RBAC middleware + gate roles (Entra live: evaluator Azure)  
- Chat narration excludes mobile, SQL, SP names  

---

## 14. Azure architecture

Implemented in code + `infra/` Bicep: Cosmos, Azure SQL, Blob (evidence + legal WORM), Service Bus, Functions, Container Apps API, Key Vault, RBAC modules, OpenAI/DI/Speech configuration hooks.

**Evaluator setup:** [docs/AZURE_EVALUATOR_SETUP.md](docs/AZURE_EVALUATOR_SETUP.md)  
**Checklist:** [docs/AZURE_DEV_EXECUTION_CHECKLIST.md](docs/AZURE_DEV_EXECUTION_CHECKLIST.md)

Submitter Azure live re-verification: **BLOCKED** (subscription unavailable). Implementation remains for evaluator use.

---

## 15. Configure evaluator Azure environment

1. Deploy `infra/` to your subscription (or use existing DEV)  
2. Bind config sections in `appsettings` / Key Vault / Container Apps env  
3. Run `dotnet run --project src/ARC.Cli -- azure-preflight AZURE_DEV`  
4. Run `dotnet run --project src/ARC.Cli -- readiness`  
5. Set `ARC_COSMOS_CONNECTION_STRING` for full integration suite  

No domain code changes required. **Do not commit secrets.**

---

## 16. Build

```powershell
dotnet restore ARC.sln
dotnet build ARC.sln -c Release
```

Stop running `ARC.Api` if DLL file-lock errors occur.

---

## 17. Run tests

**Unit / eval (821 tests):**

```powershell
dotnet test ARC.sln -c Release --filter "FullyQualifiedName!~Integration"
```

**Integration (43 tests):**

```powershell
dotnet test ARC.sln -c Release --filter "FullyQualifiedName~Integration"
```

Without Cosmos: **33 PASS / 10 FAIL** (environment-blocked).  
With Cosmos emulator: **43 PASS** expected.

---

## 18. Run CLI S1–S9

```powershell
dotnet run --project src/ARC.Cli -c Release -- all
```

Single scenario: `dotnet run --project src/ARC.Cli -c Release -- S1`

---

## 19. Run API / Web demo

**API:**

```powershell
dotnet run --project src/ARC.Api -c Release --launch-profile http
```

`http://localhost:5187` · `GET /health`

**Web chat UI:**

```powershell
dotnet run --project src/ARC.Web -c Release --launch-profile http
```

`http://localhost:5100`

Dev headers when JWT not configured: `X-Arc-Upn`, `X-Arc-Role`, `X-Arc-Region`, `X-Arc-Depot`.

---

## 20. Known TBC / blocked items

See [docs/SUBMISSION_TBC_AND_BLOCKERS.md](docs/SUBMISSION_TBC_AND_BLOCKERS.md). Summary:

- SP1 components, SP3 formula, SP5 memo SoR, SP6 legal windows  
- SP7 production Speech/field binding, SP8 court pack, **SP9 learning**  
- Production ODOS session + PTP SQL binding  
- Six blocked inline-SQL repos, MAF fan-in design  
- Azure live re-verification (evaluator can run)  

Values were **not invented** in code.

---

## 21. Current validation status

| Area | Status (2026-09-07) |
|------|---------------------|
| Release build | PASS |
| Unit / eval | 821/821 PASS |
| Integration | 33/43 PASS (10 Cosmos-blocked) |
| CLI S1–S9 | 9/9 PASS Shadow |
| Workflow A / B | PASS |
| HITL G1–G4 | PASS |
| Business Chat | PASS_LOCAL |
| Azure evaluator-ready | YES |
| Production ready | **NO** |
| Submission ready | **YES** |

Full report: [docs/FINAL_VALIDATION_REPORT.md](docs/FINAL_VALIDATION_REPORT.md)

---

## Project structure

```
src/
├── ARC.Domain           # Rules, entities, limitation clock
├── ARC.Data             # SQL, Cosmos, Blob, Service Bus, ODOS readers
├── ARC.Knowledge        # Document Intelligence, retrieval, graph
├── ARC.Tools            # Deterministic tools
├── ARC.Agents           # A1–A8, MAF workflows, Shadow gate
├── ARC.Guardrails       # MCC adapter
├── ARC.Host.Functions   # Azure Functions host
├── ARC.Api              # REST + Business Chat
├── ARC.Web              # Minimal chat UI
└── ARC.Cli              # Shadow S1–S9 runner

tests/
├── ARC.*.Tests          # Unit tests (821 excl. integration)
├── ARC.Integration.Tests
└── ARC.Eval             # Golden acceptance harness

infra/                   # Bicep IaC
SQL/ARC/                 # ARC-owned stored procedures
docs/                    # Submission + phase documentation
```

**Target framework:** .NET 9 (`net9.0`).

---

## CI

`.github/workflows/ci.yml` — build, unit tests (excl. integration), CLI S1–S9. No Azure secrets required.

---

## Safety note

> **Shadow mode is the default.** No real outbound notice, courier, court filing, or live external legal/financial action is performed by the local Shadow demonstration.

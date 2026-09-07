// ARC Workflow Demo — S1-S9 shadow scenarios (developer page only)
let currentCycleId = null;
let currentDealerUrn = null;
let pollingInterval = null;

async function startScenario(scenarioId) {
    addDevMessage(`Starting scenario ${scenarioId}...`, 'user');

    try {
        const response = await fetch('/Index?handler=StartScenario', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getAntiForgeryToken()
            },
            body: JSON.stringify({ scenarioId })
        });

        if (!response.ok) {
            addDevMessage('Failed to start scenario.', 'assistant');
            return;
        }

        const result = await response.json();
        if (result.success) {
            currentCycleId = result.cycleId;
            currentDealerUrn = result.dealerUrn;
            addDevMessage(`Scenario ${scenarioId} started. Workflow running in shadow mode.`, 'assistant');
            startPolling();
        } else {
            addDevMessage(result.error || 'Failed to start scenario.', 'assistant');
        }
    } catch (error) {
        console.error('Error starting scenario:', error);
        addDevMessage('Scenario start failed.', 'assistant');
    }
}

function startPolling() {
    if (pollingInterval) clearInterval(pollingInterval);
    pollStatus();
    pollingInterval = setInterval(pollStatus, 3000);
}

function stopPolling() {
    if (pollingInterval) {
        clearInterval(pollingInterval);
        pollingInterval = null;
    }
}

async function pollStatus() {
    if (!currentCycleId || !currentDealerUrn) return;

    try {
        const response = await fetch(
            `/Index?handler=WorkflowStatus&cycleId=${encodeURIComponent(currentCycleId)}&dealerUrn=${encodeURIComponent(currentDealerUrn)}`
        );
        if (!response.ok) return;

        const status = await response.json();
        updateWorkflowUI(status);

        if (status.status === 'Completed' || status.status === 'Terminated') {
            stopPolling();
            if (status.status === 'Completed') showShadowActions();
        }
    } catch (error) {
        console.error('Error polling status:', error);
    }
}

function updateWorkflowUI(status) {
    const existing = document.querySelector('.workflow-progress');
    if (existing) existing.remove();

    const card = document.createElement('div');
    card.className = 'workflow-progress';

    const nodes = [
        { name: 'A1', display: 'Reconciliation' },
        { name: 'A2', display: 'Risk Assessment' },
        { name: 'A3', display: 'Notice Decision' },
        { name: 'G1', display: 'Depot Manager Approval' },
        { name: 'A5', display: 'Drafting' },
        { name: 'G2', display: 'Advocate Review' },
        { name: 'A6', display: 'Field Action' }
    ];

    const completedNodes = status.completedNodes || [];
    let nodesHtml = '';
    nodes.forEach((node) => {
        let statusClass = 'waiting';
        let icon = '○';
        if (completedNodes.includes(node.name)) {
            statusClass = 'completed';
            icon = '✓';
        } else if (status.waitingGate && status.waitingGate.includes(node.name)) {
            statusClass = 'pending';
            icon = '⏳';
        }
        nodesHtml += `<div class="workflow-node"><span class="node-status-icon node-${statusClass}">${icon}</span><span class="node-name">${node.name} ${node.display}</span></div>`;
    });

    let statusClass = 'running';
    let statusText = status.status;
    if (status.status === 'WaitingForHuman') {
        statusClass = 'waiting';
        statusText = 'Waiting for approval';
    } else if (status.status === 'Completed') {
        statusClass = 'completed';
        statusText = 'Workflow completed';
    }

    card.innerHTML = `<h4>Workflow Progress</h4>${nodesHtml}<div class="workflow-status ${statusClass}">${statusText}</div>`;
    document.getElementById('chatMessages')?.appendChild(card);

    if (status.waitingGate) showGate(status.waitingGate);
}

function showGate(gateId) {
    if (document.querySelector('.gate-card')) return;

    const gateNames = {
        G1: 'Depot Manager Approval',
        G2: 'Advocate Signature',
        G3: 'Legal Eligibility Review',
        G4: 'Case Filing Authorization'
    };

    const card = document.createElement('div');
    card.className = 'gate-card';
    card.innerHTML = `
        <h4>${gateId} — ${gateNames[gateId] || gateId}</h4>
        <div class="gate-actions">
            <button type="button" class="btn btn-success btn-sm" onclick="submitGateDecision('${gateId}', 'Approved')">Approve</button>
            <button type="button" class="btn btn-danger btn-sm" onclick="submitGateDecision('${gateId}', 'Rejected')">Reject</button>
        </div>`;
    document.getElementById('chatMessages')?.appendChild(card);
}

async function submitGateDecision(gateId, decision) {
    if (!currentCycleId || !currentDealerUrn) return;

    addDevMessage(`${decision} ${gateId}`, 'user');

    try {
        const response = await fetch('/Index?handler=SubmitGateDecision', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getAntiForgeryToken()
            },
            body: JSON.stringify({
                cycleId: currentCycleId,
                dealerUrn: currentDealerUrn,
                gateId,
                decision
            })
        });

        if (!response.ok) {
            addDevMessage('Gate decision failed.', 'assistant');
            return;
        }

        const result = await response.json();
        if (result.success) {
            document.querySelector('.gate-card')?.remove();
            addDevMessage(`${gateId} ${decision.toLowerCase()}. Resuming workflow.`, 'assistant');
            startPolling();
        }
    } catch (error) {
        console.error('Error submitting gate decision:', error);
    }
}

async function showShadowActions() {
    try {
        const response = await fetch('/Index?handler=ShadowActions');
        const result = await response.json();
        if (result.actions?.length > 0) {
            const card = document.createElement('div');
            card.className = 'shadow-actions-card';
            card.innerHTML = `<h4>Suppressed outbound actions</h4><p>${result.actions.length} action(s) recorded but not dispatched.</p>`;
            document.getElementById('chatMessages')?.appendChild(card);
        }
    } catch (error) {
        console.error('Error getting shadow actions:', error);
    }
}

function addDevMessage(text, role) {
    const div = document.createElement('div');
    div.className = `dev-msg dev-msg-${role}`;
    div.textContent = text;
    document.getElementById('chatMessages')?.appendChild(div);
}

function getAntiForgeryToken() {
    return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
}

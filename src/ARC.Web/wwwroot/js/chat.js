// ARC Recovery Copilot - Premium Business Chat UI
let conversationId = null;
let typingPhraseTimer = null;

const conversationContext = {
    dealerCode: null,
    dealerName: null,
    depotCode: null,
    depotName: null,
    period: null,
    lastTopic: null,
    mode: 'Shadow'
};

const TYPING_PHRASES = [
    'ARC is reviewing the available business data',
    'Reviewing approved business data...',
    'Preparing recovery summary...',
    'Finalizing the response...'
];

document.addEventListener('DOMContentLoaded', () => {
    bindComposer();
    bindPromptChips();
    bindContextToggle();
    updateWelcomeGreeting();
    checkApiHealth();
});

function updateWelcomeGreeting() {
    const el = document.getElementById('welcomeGreeting');
    if (!el) return;
    const hour = new Date().getHours();
    let salutation = 'Good evening';
    if (hour < 12) salutation = 'Good morning';
    else if (hour < 18) salutation = 'Good afternoon';
    el.textContent = `\u{1F44B} ${salutation}!`;
}

function bindComposer() {
    const input = document.getElementById('messageInput');
    if (!input) return;

    input.addEventListener('keydown', (event) => {
        if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault();
            sendMessage();
        }
    });

    input.addEventListener('input', autoResizeInput);
}

function bindPromptChips() {
    document.querySelectorAll('.arc-chip[data-prompt]').forEach((chip) => {
        chip.addEventListener('click', () => {
            const prompt = chip.getAttribute('data-prompt');
            if (!prompt || isChatLoading()) return;
            const input = document.getElementById('messageInput');
            if (input) input.value = prompt;
            sendMessage();
        });
    });
}

function bindContextToggle() {
    const toggle = document.getElementById('contextToggle');
    const panel = document.querySelector('.arc-context-panel');
    if (!toggle || !panel) return;
    toggle.addEventListener('click', () => panel.classList.toggle('open'));
}

async function checkApiHealth() {
    const dot = document.getElementById('apiStatusDot');
    const text = document.getElementById('apiStatusText');
    try {
        const response = await fetch('/Index?handler=ApiHealth');
        const data = await response.json();
        if (data.healthy) {
            dot?.classList.add('healthy');
            dot?.classList.remove('unhealthy');
            if (text) text.textContent = 'API Connected';
        } else {
            dot?.classList.add('unhealthy');
            dot?.classList.remove('healthy');
            if (text) text.textContent = 'ARC API unavailable';
        }
    } catch {
        dot?.classList.add('unhealthy');
        dot?.classList.remove('healthy');
        if (text) text.textContent = 'ARC API unavailable';
    }
}

function autoResizeInput() {
    const input = document.getElementById('messageInput');
    if (!input) return;
    input.style.height = 'auto';
    input.style.height = `${Math.min(input.scrollHeight, 140)}px`;
}

async function sendMessage() {
    const input = document.getElementById('messageInput');
    const text = input?.value.trim();
    if (!text || isChatLoading()) return;

    addUserMessage(text);
    input.value = '';
    autoResizeInput();

    await sendBusinessChat(text);
}

async function sendBusinessChat(message) {
    setChatLoading(true);
    try {
        const response = await fetch('/Index?handler=BusinessChat', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getAntiForgeryToken()
            },
            body: JSON.stringify({ message, conversationId })
        });

        if (!response.ok) {
            const payload = await response.json().catch(() => ({}));
            addAssistantMessage(renderErrorCard(mapHttpError(response.status, payload)));
            return;
        }

        const result = await response.json();
        if (result.error) {
            addAssistantMessage(renderErrorCard(mapProxyError(result)));
            return;
        }
        conversationId = result.conversationId || conversationId;
        if (result.runMode) conversationContext.mode = result.runMode;
        if (isBusinessResponse(result)) {
            updateContextFromResult(result, message);
        }
        addAssistantMessage(formatBusinessResponse(result));
    } catch {
        addAssistantMessage(renderErrorCard('ARC service is temporarily unavailable.<br />Please try again.'));
    } finally {
        setChatLoading(false);
    }
}

function formatBusinessResponse(result) {
    if (result.safetyOutcome === 'BlockedAuthorization') {
        return renderInfoCard("You don't have access to this information.", 'shield');
    }

    if (isBusinessUnavailableResponse(result)) {
        return renderBusinessUnavailableCard(result.answer);
    }

    if (isFailClosedResponse(result)) {
        return renderFailClosedCard();
    }

    if (result.facts && hasRenderableFacts(result.facts)) {
        return renderStructuredResponse(result);
    }

    const text = sanitizeAnswerText(result.answer || '');
    if (!text) {
        return renderInfoCard("I couldn't confirm this information from the currently approved ARC capabilities.", 'info');
    }
    return renderTextResponse(text);
}

function isBusinessResponse(result) {
    if (result.groundedOnDeterministicFacts) return true;
    return !!(result.facts && hasRenderableFacts(result.facts));
}

function isConversationalResponse(result) {
    return result.safetyOutcome === 'Allowed'
        && !result.groundedOnDeterministicFacts
        && !(result.facts && hasRenderableFacts(result.facts));
}

function isBusinessUnavailableResponse(result) {
    if (isConversationalResponse(result)) return false;
    const answer = (result.answer || '').toLowerCase();
    if (result.groundedOnDeterministicFacts === false && !result.facts) return true;
    if (answer.includes('cannot currently be confirmed') && !isFailClosedResponse(result)) return true;
    if (answer.includes('not available from') || answer.includes('could not be confirmed')) return true;
    return false;
}

function isFailClosedResponse(result) {
    const answer = (result.answer || '').toLowerCase();
    const exposure = result.facts?.exposure;
    if (exposure?.missingComponents?.length > 0 && exposure.netRecoverableExposure == null) return true;
    if (answer.includes('net recoverable') && (answer.includes('cannot') || answer.includes('not available') || answer.includes('unavailable'))) return true;
    if (answer.includes('cannot currently be confirmed') || answer.includes('will not estimate')) return true;
    if (result.safetyOutcome?.startsWith('Blocked')) return true;
    return false;
}

function hasRenderableFacts(facts) {
    return !!(facts.identity || facts.financial || facts.recovery || facts.field || facts.legal || facts.evidence);
}

function renderStructuredResponse(result) {
    const facts = result.facts;
    const id = facts.identity;
    const intro = buildResponseIntro(facts, result);
    const metrics = buildMetricCards(facts);
    const details = buildAdditionalDetails(facts);
    const time = formatTime(new Date());

    return `
<div class="arc-response-card arc-animate-in">
    <div class="arc-response-intro">${intro}</div>
    ${id ? renderDealerHeader(id) : ''}
    ${metrics ? `<div class="arc-metric-grid">${metrics}</div>` : ''}
    ${renderResponseBottom(details, time)}
</div>`;
}

function buildResponseIntro(facts, result) {
    const id = facts.identity;
    const fin = facts.financial;
    const rec = facts.recovery;
    const field = facts.field;
    const topic = (conversationContext.lastTopic || '').toLowerCase();
    const dealerName = id?.dealerName || conversationContext.dealerName;

    if (topic.includes('ageing') && fin) {
        return 'Here is the ageing position from the approved outstanding data.';
    }
    if ((topic.includes('recovery') || topic.includes('notice')) && (rec || fin)) {
        return 'Here is the latest available recovery and notice status.';
    }
    if ((topic.includes('field') || topic.includes('tsi') || topic.includes('visit')) && field) {
        return 'Here is the latest available field activity.';
    }
    if (fin && !id) {
        return dealerName
            ? `Here is the latest available outstanding position for <strong>${escapeHtml(dealerName)}</strong>.`
            : 'Here is the latest available outstanding position for the selected dealer.';
    }
    if (fin && id) {
        return dealerName
            ? `Here is the latest available outstanding position for <strong>${escapeHtml(dealerName)}</strong>.`
            : `Here is the latest available outstanding position for <strong>${escapeHtml(id.dealerCode)}</strong>.`;
    }
    if (id) {
        return `Here is the dealer information for <strong>${escapeHtml(id.dealerCode)}</strong> in depot <strong>${escapeHtml(id.depotCode)}</strong>.`;
    }
    if (rec) return 'Here is the latest available recovery and notice status.';
    if (field) return 'Here is the latest available field activity.';
    const text = sanitizeAnswerText(result.answer || '');
    return text.replace(/<p>/g, '').replace(/<\/p>/g, ' ') || 'Here is the information from approved sources.';
}

function renderDealerHeader(id) {
    const depotCode = escapeHtml(id.depotCode || '');
    const depotName = escapeHtml(id.depotName || '');
    const depotLine = depotName ? `${depotCode} \u2013 ${depotName}` : depotCode;
    return `
<div class="arc-dealer-header">
    <div class="arc-dealer-icon"><i class="bi bi-building"></i></div>
    <div class="arc-dealer-info">
        <div class="arc-dealer-name">${escapeHtml(id.dealerName || '\u2014')}</div>
        <div class="arc-dealer-meta">
            <span>Dealer Code: <strong>${escapeHtml(id.dealerCode)}</strong></span>
            <span class="arc-sep">|</span>
            <span>Depot: <strong>${depotLine}</strong></span>
        </div>
    </div>
</div>`;
}

function renderResponseBottom(details, time) {
    const detailsBlock = details.length
        ? `<details class="arc-details">
    <summary><i class="bi bi-chevron-down"></i> Additional Details</summary>
    <div class="arc-detail-grid">${details.map((d) => `
        <div class="arc-detail-cell">
            <span class="arc-detail-label">${escapeHtml(d.label)}</span>
            <strong>${escapeHtml(d.value)}</strong>
        </div>`).join('')}
    </div>
</details>`
        : '';

    return `
<div class="arc-response-bottom">
    ${detailsBlock}
    <div class="arc-response-meta-row">
        <span>Data from approved sources</span>
        <span>${time}</span>
    </div>
</div>`;
}

function buildMetricCards(facts) {
    const cards = [];
    const fin = facts.financial;
    const rec = facts.recovery;
    const field = facts.field;
    const legal = facts.legal;

    if (fin) {
        if (fin.currentOutstanding != null) {
            cards.push(metricCard('bi-currency-rupee', 'Current Outstanding', formatInr(fin.currentOutstanding), 'As on latest available period', 'accent-blue'));
        }
        if (fin.over90Outstanding != null) {
            cards.push(metricCard('bi-graph-up-arrow', '&gt;90 Outstanding', formatInr(fin.over90Outstanding), 'Ageing bucket &gt; 90 days', 'accent-red'));
        }
        if (fin.businessLineLimit != null) {
            cards.push(metricCard('bi-sliders', 'Business Line Limit', formatInr(fin.businessLineLimit), 'Configured limit', 'accent-navy'));
        }
    }

    if (rec) {
        const notice = formatNoticeStatus(rec);
        if (notice) {
            cards.push(metricCard('bi-file-earmark-text', 'Demand Notice', escapeHtml(notice), 'Latest status available', 'accent-purple'));
        }
        const recovery = formatRecoveryStatus(rec);
        if (recovery) {
            cards.push(metricCard('bi-arrow-repeat', 'Recovery Status', escapeHtml(recovery), 'From approved recovery data', 'accent-orange'));
        }
    }

    if (field) {
        if (field.visitStatus != null && field.visitStatus !== '') {
            const sub = field.tsiVisitCount != null ? `Visit count: ${field.tsiVisitCount}` : 'From approved visit data';
            cards.push(metricCard('bi-geo-alt', 'TSI Visit Status', escapeHtml(field.visitStatus), escapeHtml(sub), 'accent-blue'));
        } else if (field.tsiVisitCount != null) {
            cards.push(metricCard('bi-hash', 'TSI Visit Count', String(field.tsiVisitCount), 'Recorded visits', 'accent-blue'));
        }
    }

    if (fin && hasAgeingBuckets(fin) && shouldShowAgeingBreakdown()) {
        cards.push(metricCard('bi-hourglass-split', '&lt;90 days', formatInr(fin.outstandingBucket0), 'Ageing bucket', 'accent-muted'));
        cards.push(metricCard('bi-hourglass', '90\u2013120 days', formatInr(fin.outstandingBucket1), 'Ageing bucket', 'accent-muted'));
        cards.push(metricCard('bi-hourglass', '121\u2013180 days', formatInr(fin.outstandingBucket2), 'Ageing bucket', 'accent-muted'));
        cards.push(metricCard('bi-hourglass', '181\u2013365 days', formatInr(fin.outstandingBucket3), 'Ageing bucket', 'accent-muted'));
        cards.push(metricCard('bi-hourglass-bottom', '&gt;365 days', formatInr(fin.outstandingBucket4), 'Ageing bucket', 'accent-muted'));
    }

    if (rec) {
        const legalStatus = formatLegalStatus(rec);
        if (legalStatus && !cards.some((c) => c.includes('Legal Status'))) {
            cards.push(metricCard('bi-balance-scale', 'Legal Status', escapeHtml(legalStatus), 'From approved legal data', 'accent-purple'));
        }
    }

    if (field?.dealerFeedback) {
        cards.push(metricCard('bi-chat-left-text', 'Dealer Feedback', escapeHtml(field.dealerFeedback), 'From approved source', 'accent-muted'));
    }

    if (legal?.section138Eligible != null) {
        const val = legal.section138Eligible ? 'Eligible' : 'Not established';
        cards.push(metricCard('bi-shield-check', 'Section 138', val, escapeHtml(legal.eligibilityReason || 'Eligibility assessment'), 'accent-purple'));
    }

    if (facts.evidence?.completenessScore != null) {
        cards.push(metricCard('bi-folder-check', 'Evidence Completeness', `${facts.evidence.completenessScore}%`, 'Case file score', 'accent-navy'));
    }

    return cards.join('');
}

function metricCard(icon, label, value, hint, accent) {
    return `
<div class="arc-metric-card ${accent}">
    <div class="arc-metric-label"><i class="bi ${icon}"></i> ${label}</div>
    <div class="arc-metric-value">${value}</div>
    <div class="arc-metric-hint">${hint}</div>
</div>`;
}

function hasAgeingBuckets(fin) {
    return fin.outstandingBucket0 != null || fin.outstandingBucket1 != null;
}

function shouldShowAgeingBreakdown() {
    const topic = (conversationContext.lastTopic || '').toLowerCase();
    return topic.includes('ageing') || topic.includes('outstanding');
}

function formatNoticeStatus(rec) {
    if (rec.noticeGeneratedYn === 'Y') return 'Generated';
    if (rec.noticeGeneratedYn === 'N') return 'Not Generated';
    if (rec.noticeDepotYn || rec.noticeHoYn) {
        const parts = [];
        if (rec.noticeDepotYn) parts.push(`Depot: ${rec.noticeDepotYn}`);
        if (rec.noticeHoYn) parts.push(`HO: ${rec.noticeHoYn}`);
        return parts.join(' / ');
    }
    return null;
}

function formatRecoveryStatus(rec) {
    if (rec.recoveryStatusDescription) return rec.recoveryStatusDescription;
    if (rec.recoveryStatusCode) return rec.recoveryStatusCode;
    return null;
}

function formatLegalStatus(rec) {
    if (rec.legalStatusDescription) return rec.legalStatusDescription;
    if (rec.legalStatusCode) return rec.legalStatusCode;
    return null;
}

function buildAdditionalDetails(facts) {
    const items = [];
    const id = facts.identity;
    if (id?.billTo) items.push({ label: 'Bill To', value: id.billTo });
    if (id?.customerType) items.push({ label: 'Customer Type', value: id.customerType });
    if (id?.territoryName) {
        const terr = id.territoryCode ? `${id.territoryCode} (${id.territoryName})` : id.territoryName;
        items.push({ label: 'Territory', value: terr });
    }
    if (id?.regionName || id?.depotRegion) {
        items.push({ label: 'Region', value: [id.depotRegion, id.regionName].filter(Boolean).join(' \u2013 ') });
    }
    return items;
}

function renderDetailsAccordion(innerHtml) {
    return innerHtml;
}

function renderFailClosedCard() {
    return `
<div class="arc-info-card arc-fail-closed arc-animate-in">
    <div class="arc-info-icon"><i class="bi bi-shield-exclamation"></i></div>
    <div>
        <p class="arc-info-title">Net recoverable exposure cannot currently be confirmed from the approved authoritative data sources.</p>
        <p class="arc-info-sub">ARC will not estimate or fabricate this financial value.</p>
    </div>
</div>`;
}

function renderBusinessUnavailableCard(answer) {
    const secondary = sanitizeAnswerText(answer);
    return `
<div class="arc-info-card arc-business-unavailable arc-animate-in">
    <div class="arc-info-icon"><i class="bi bi-database-x"></i></div>
    <div>
        <p class="arc-info-title">This information cannot currently be confirmed from the approved authoritative data sources.</p>
        ${secondary ? `<div class="arc-info-extra">${secondary}</div>` : ''}
    </div>
</div>`;
}

function renderInfoCard(message, icon = 'info') {
    const iconClass = icon === 'shield' ? 'bi-shield' : 'bi-info-circle';
    return `
<div class="arc-info-card arc-animate-in">
    <div class="arc-info-icon"><i class="bi ${iconClass}"></i></div>
    <p>${message}</p>
</div>`;
}

function renderErrorCard(message) {
    return `
<div class="arc-info-card arc-error-card arc-animate-in">
    <div class="arc-info-icon"><i class="bi bi-exclamation-triangle"></i></div>
    <p>${message}</p>
</div>`;
}

function renderTextResponse(html) {
    return `<div class="arc-response-card arc-text-only arc-animate-in"><div class="arc-response-body">${html}</div></div>`;
}

function updateContextFromResult(result, userMessage) {
    const id = result.facts?.identity;
    const fin = result.facts?.financial;
    if (id) {
        conversationContext.dealerCode = id.dealerCode;
        conversationContext.dealerName = id.dealerName;
        conversationContext.depotCode = id.depotCode;
        conversationContext.depotName = id.depotName;
    }
    if (fin?.periodKey) conversationContext.period = formatPeriod(fin.periodKey);
    if (result.runMode) conversationContext.mode = result.runMode;
    if (userMessage) conversationContext.lastTopic = mapBusinessTopic(userMessage);
    renderContextPanel();
}

function mapBusinessTopic(message) {
    const m = (message || '').toLowerCase();
    if (m.includes('outstanding') || m.includes('ageing') || m.includes('over 90')) return 'Outstanding Analysis';
    if (m.includes('notice') || m.includes('recovery') || m.includes('legal')) return 'Recovery Status';
    if (m.includes('visit') || m.includes('tsi') || m.includes('field') || m.includes('ptp')) return 'Field Activity';
    if (m.includes('dealer')) return 'Dealer Overview';
    if (m.includes('exposure')) return 'Financial Adjustments';
    if (m.includes('evidence')) return 'Evidence';
    return conversationContext.lastTopic || 'General';
}

function renderContextPanel() {
    const dealerEl = document.getElementById('ctxDealer');
    const depotEl = document.getElementById('ctxDepot');
    const periodEl = document.getElementById('ctxPeriod');
    const topicEl = document.getElementById('ctxTopic');
    const modeEl = document.getElementById('ctxMode');

    if (dealerEl) {
        dealerEl.innerHTML = conversationContext.dealerCode
            ? `<strong>${escapeHtml(conversationContext.dealerCode)}</strong><br />${escapeHtml(conversationContext.dealerName || '')}`
            : '<span class="arc-muted">Not selected</span>';
    }
    if (depotEl) {
        depotEl.innerHTML = conversationContext.depotCode
            ? `<strong>${escapeHtml(conversationContext.depotCode)}</strong><br />${escapeHtml(conversationContext.depotName || '')}`
            : '<span class="arc-muted">Not selected</span>';
    }
    if (periodEl) {
        periodEl.innerHTML = conversationContext.period
            ? `<strong>${escapeHtml(conversationContext.period)}</strong>`
            : 'Current session';
    }
    if (topicEl) topicEl.innerHTML = conversationContext.lastTopic
        ? `<strong>${escapeHtml(conversationContext.lastTopic)}</strong>`
        : '\u2014';
    if (modeEl) modeEl.innerHTML = `<strong>${escapeHtml(conversationContext.mode || 'Shadow')}</strong>`;
}

function clearContext() {
    conversationContext.dealerCode = null;
    conversationContext.dealerName = null;
    conversationContext.depotCode = null;
    conversationContext.depotName = null;
    conversationContext.period = null;
    conversationContext.lastTopic = null;
    renderContextPanel();
}

function newConversation() {
    conversationId = null;
    clearContext();
    const messages = document.getElementById('chatMessages');
    if (messages) messages.innerHTML = '';
    showWelcomeHero();
    updateWelcomeGreeting();
    scrollToBottom();
}

function hideWelcomeHero() {
    document.getElementById('welcomeHero')?.classList.add('hidden');
}

function showWelcomeHero() {
    document.getElementById('welcomeHero')?.classList.remove('hidden');
}

function setChatLoading(isLoading) {
    const indicator = document.getElementById('chatLoading');
    const input = document.getElementById('messageInput');
    const sendBtn = document.querySelector('.arc-send-btn');
    indicator?.classList.toggle('d-none', !isLoading);
    if (input) input.disabled = isLoading;
    if (sendBtn) sendBtn.disabled = isLoading;

    if (isLoading) {
        startTypingPhrases();
        scrollToBottom();
    } else {
        stopTypingPhrases();
    }
}

function startTypingPhrases() {
    let idx = 0;
    const phraseEl = document.getElementById('typingPhrase');
    if (!phraseEl) return;
    phraseEl.textContent = TYPING_PHRASES[0];
    typingPhraseTimer = window.setInterval(() => {
        idx = (idx + 1) % TYPING_PHRASES.length;
        phraseEl.textContent = TYPING_PHRASES[idx];
    }, 2200);
}

function stopTypingPhrases() {
    if (typingPhraseTimer) {
        clearInterval(typingPhraseTimer);
        typingPhraseTimer = null;
    }
}

function isChatLoading() {
    const indicator = document.getElementById('chatLoading');
    return indicator && !indicator.classList.contains('d-none');
}

function addUserMessage(content) {
    const time = formatTime(new Date());
    const row = document.createElement('div');
    row.className = 'arc-msg-user arc-animate-in';
    row.innerHTML = `
<div class="arc-user-bubble">
    <div class="arc-user-text">${escapeHtml(content).replace(/\n/g, '<br>')}</div>
    <div class="arc-user-meta">
        <span>${time}</span>
        <span class="arc-read-receipt" aria-hidden="true">\u2713\u2713</span>
    </div>
</div>
<div class="arc-user-avatar" aria-hidden="true">U</div>`;
    document.getElementById('chatMessages')?.appendChild(row);
    scrollToBottom();
}

function addAssistantMessage(contentHtml) {
    const row = document.createElement('div');
    row.className = 'arc-msg-assistant arc-animate-in';
    row.innerHTML = `
<div class="arc-assistant-avatar" aria-hidden="true">${arcLogoInAvatar('msg')}</div>
<div class="arc-assistant-body">${contentHtml}</div>`;
    document.getElementById('chatMessages')?.appendChild(row);
    scrollToBottom();
}

function scrollToBottom() {
    const panel = document.getElementById('chatScroll');
    if (panel) panel.scrollTop = panel.scrollHeight;
}

function formatTime(date) {
    return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

function formatInr(amount) {
    if (amount == null || Number.isNaN(Number(amount))) return '\u2014';
    return `<strong>${new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 2 }).format(Number(amount))}</strong>`;
}

function formatPeriod(periodKey) {
    if (!periodKey) return 'Current session';
    const match = /^(\d{4})-(\d{2})$/.exec(periodKey);
    if (!match) return periodKey;
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    const month = months[parseInt(match[2], 10) - 1] || match[2];
    return `${month} ${match[1]}`;
}

function sanitizeAnswerText(answer) {
    const lines = (answer || '').split(/\r?\n/)
        .map((line) => line.trim())
        .filter((line) => line.length > 0)
        .filter((line) => !/^source:/i.test(line))
        .filter((line) => !/usp_/i.test(line))
        .filter((line) => !/getdealer/i.test(line))
        .filter((line) => !/capability/i.test(line))
        .filter((line) => !/mcp/i.test(line))
        .filter((line) => !/dec-o/i.test(line))
        .filter((line) => !/dec-f/i.test(line))
        .filter((line) => !/authoritative_/i.test(line))
        .filter((line) => !/missing component/i.test(line))
        .filter((line) => !/required adjustment/i.test(line))
        .filter((line) => !/not available \u2014/i.test(line))
        .filter((line) => !/tool/i.test(line) || line.length < 20);

    if (lines.length === 0) return '';
    return lines.map((line) => `<p>${escapeHtml(line)}</p>`).join('');
}

function mapHttpError(status, payload) {
    if (status === 503 || status === 502) {
        return payload?.error || 'ARC service is temporarily unavailable.<br />Please try again.';
    }
    if (status === 401 || status === 403) {
        return "You don't have access to this information.";
    }
    return 'ARC service is temporarily unavailable.<br />Please try again.';
}

function mapProxyError(payload) {
    if (payload.errorType === 'api_unreachable') {
        return 'ARC service is temporarily unavailable.<br />Please try again.';
    }
    return payload.error || 'ARC service is temporarily unavailable.<br />Please try again.';
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text ?? '';
    return div.innerHTML;
}

function getAntiForgeryToken() {
    return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
}

// Expose for inline handlers
window.sendMessage = sendMessage;
window.newConversation = newConversation;
window.clearContext = clearContext;

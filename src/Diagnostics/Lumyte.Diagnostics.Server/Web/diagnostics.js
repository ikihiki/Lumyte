const byId = id => document.getElementById(id);
const titles = { overview: '概要', operations: 'Operations', input: 'Input', metrics: 'Metrics', logs: 'Logs', traces: 'Traces' };
let csrf = '';
let authenticated = false;
let sessions = [];
let selectedId = '';
let events = [];
let source = null;
let activeTab = 'overview';
let loadingSessions = null;
let selectionVersion = 0;
let authenticationVersion = 0;

function element(tag, text, className) {
    const node = document.createElement(tag);
    if (text !== undefined) node.textContent = text;
    if (className) node.className = className;
    return node;
}

function valueOf(value) {
    if (!value) return '';
    return [() => String(value.boolean), () => value.int64, () => String(value.double), () => value.string][value.kind]?.() ?? '?';
}

async function api(path, options = {}) {
    const headers = new Headers(options.headers);
    if (options.method && options.method !== 'GET') headers.set('X-Diagnostics-CSRF', csrf);
    const response = await fetch('/api/ui' + path, { ...options, headers, credentials: 'same-origin', cache: 'no-store' });
    if (response.status === 401) {
        if (path !== '/login') showLogin();
        throw new Error('認証できません。操作用トークンを確認してログインしてください。');
    }
    if (!response.ok) throw new Error(`要求を処理できませんでした（HTTP ${response.status}）。`);
    return response.status === 204 ? null : response.json();
}

function stopStream() {
    source?.close();
    source = null;
}

function showLogin() {
    authenticated = false;
    authenticationVersion++;
    stopStream();
    selectionVersion++;
    sessions = [];
    selectedId = '';
    events = [];
    byId('login-panel').hidden = false;
    byId('workspace').hidden = true;
    byId('logout').hidden = true;
    byId('connection-status').textContent = '未ログイン';
    byId('operation-result').textContent = '';
    byId('event-detail').textContent = 'イベントを選択してください。';
    renderSession();
}

async function bootstrap() {
    const state = await api('/bootstrap');
    csrf = state.requestToken;
    authenticated = state.authenticated;
    if (!authenticated) { showLogin(); document.querySelector("#login-form button").disabled = false; return; }
    byId('login-panel').hidden = true;
    byId('workspace').hidden = false;
    byId('logout').hidden = false;
    await loadSessions();
    startStream();
}

function selected() { return sessions.find(session => session.sessionId === selectedId); }

async function loadSessions() {
    if (loadingSessions) return loadingSessions;
    const version = authenticationVersion;
    loadingSessions = (async () => {
        const loaded = await api('/sessions');
        if (!authenticated || version !== authenticationVersion) return;
        sessions = loaded;
        const select = byId('game-select');
        select.replaceChildren();
        if (!sessions.length) select.append(element('option', 'ゲームを待っています'));
        for (const session of sessions) {
            const option = element('option', `Game ${session.instanceId.slice(0, 8)} · ${session.catalog.length} subsystems`);
            option.value = session.sessionId;
            select.append(option);
        }
        const next = sessions.some(session => session.sessionId === selectedId) ? selectedId : sessions[0]?.sessionId ?? '';
        if (next !== selectedId) {
            selectedId = next;
            events = [];
            selectionVersion++;
            byId('event-detail').textContent = 'イベントを選択してください。';
            byId('operation-result').textContent = '';
            byId('operation-status').textContent = '操作を実行すると結果を表示します。';
        }
        select.value = selectedId;
        renderSession();
    })();
    try { await loadingSessions; } finally { loadingSessions = null; }
}

function renderSession() {
    const session = selected();
    byId('instance-id').textContent = session?.instanceId ?? '—';
    byId('session-id').textContent = session?.sessionId ?? '—';
    byId('disconnect').disabled = !session;
    byId('received').textContent = session?.telemetryReceived ?? '0';
    byId('dropped').textContent = session?.telemetryDropped ?? '0';
    byId('pending').textContent = String(session?.pendingCommands ?? 0);
    byId('subsystems').replaceChildren(...(session?.catalog ?? []).map(item => element('li', `${item.subsystem.displayName} · ${item.operations.length} operations`)));
    const operations = (session?.catalog ?? []).flatMap(item => item.operations.map(operation => ({ subsystem: item.subsystem, operation })));
    byId('catalog-summary').textContent = session ? `${session.catalog.length} サブシステム / ${operations.length} 操作を公開しています。` : 'ゲームを接続すると、公開カタログを表示します。';
    renderForms(byId('operation-forms'), operations);
    renderForms(byId('input-forms'), operations.filter(item => item.operation.requiredPermission === 2));
    renderEvents();
}

function scalarInput(field) {
    const input = field.kind === 0 ? element('select') : element('input');
    if (field.kind === 0) {
        for (const value of ['true', 'false']) { const option = element('option', value); option.value = value; input.append(option); }
    } else {
        input.type = field.kind === 2 ? 'number' : 'text';
        input.required = true;
        if (field.kind === 1) { input.inputMode = 'numeric'; input.pattern = '-?[0-9]+'; }
        if (field.kind === 2) { input.step = 'any'; if (field.minimum != null) input.min = field.minimum; if (field.maximum != null) input.max = field.maximum; }
        if (field.kind === 3 && field.maxLength != null) input.maxLength = field.maxLength;
    }
    input.name = field.id;
    input.setAttribute('aria-label', field.id);
    return input;
}

function readScalar(field, input) {
    const raw = input.value;
    if (field.kind === 0) return { kind: 0, boolean: raw === 'true' };
    if (field.kind === 1) {
        if (!/^-?[0-9]+$/.test(raw)) throw new Error(`${field.id}: 整数を入力してください。`);
        const value = BigInt(raw);
        if (value < -(1n << 63n) || value > (1n << 63n) - 1n) throw new Error(`${field.id}: Int64の範囲を超えています。`);
        if ((field.minimum != null && value < BigInt(Math.ceil(field.minimum))) || (field.maximum != null && value > BigInt(Math.floor(field.maximum)))) throw new Error(`${field.id}: 公開された範囲内で入力してください。`);
        return { kind: 1, int64: value.toString() };
    }
    if (field.kind === 2) {
        const value = Number(raw);
        if (!raw || !Number.isFinite(value)) throw new Error(`${field.id}: 有限の数値を入力してください。`);
        return { kind: 2, double: value };
    }
    return { kind: 3, string: raw };
}

function renderForms(container, operations) {
    container.replaceChildren();
    if (!operations.length) { container.append(element('p', '公開された操作はありません。', 'hint')); return; }
    const query = byId(container.id === 'input-forms' ? 'input-filter' : 'operation-filter').value.toLocaleLowerCase();
    const matching = operations.filter(item => `${item.subsystem.displayName} ${item.subsystem.id} ${item.operation.displayName} ${item.operation.id}`.toLocaleLowerCase().includes(query));
    if (matching.length > 20) container.append(element('p', `${matching.length} 操作中の先頭20操作を表示しています。検索で絞り込めます。`, 'hint'));
    if (!matching.length) container.append(element('p', '該当する操作はありません。', 'hint'));
    for (const { subsystem, operation } of matching.slice(0, 20)) {
        const card = element('article', undefined, 'card');
        card.append(element('p', subsystem.displayName, 'eyebrow'), element('h2', operation.displayName), element('p', operation.id, 'hint'));
        const form = element('form');
        const inputs = [];
        for (const field of operation.arguments) {
            const label = element('label', field.id);
            const input = scalarInput(field);
            label.append(input);
            form.append(label);
            const constraints = [`型: ${['Boolean', 'Int64', 'Double', 'String'][field.kind]}`];
            if (field.minimum != null) constraints.push(`最小 ${field.minimum}`);
            if (field.maximum != null) constraints.push(`最大 ${field.maximum}`);
            if (field.maxLength != null) constraints.push(`最大 ${field.maxLength} 文字`);
            form.append(element('p', constraints.join(' / '), 'field-hint'));
            inputs.push({ field, input });
        }
        let revision;
        if (operation.requiresRevision) {
            const label = element('label', 'Expected revision');
            revision = scalarInput({ kind: 1, id: 'expected-revision' });
            label.append(revision);
            form.append(label);
        }
        const submit = element('button', '実行', 'primary'); submit.type = 'submit';
        const message = element('p', '', 'error'); message.setAttribute('role', 'alert');
        form.append(submit, message);
        form.addEventListener('submit', async event => {
            event.preventDefault();
            message.textContent = '';
            const target = selectedId;
            const version = selectionVersion;
            submit.disabled = true;
            try {
                const argumentsMap = Object.fromEntries(inputs.map(({ field, input }) => [field.id, readScalar(field, input)]));
                const request = { requestId: crypto.randomUUID(), subsystemId: subsystem.id, operationId: operation.id, arguments: argumentsMap, timeoutMilliseconds: 5000 };
                if (revision) request.expectedRevision = readScalar({ kind: 1, id: 'expected-revision' }, revision).int64;
                byId('operation-status').textContent = 'ゲームの実行結果を待っています…';
                const result = await api(`/sessions/${target}/operations`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request) });
                if (version === selectionVersion && authenticated) {
                    byId('operation-status').textContent = `${result.status}${result.code ? ' · ' + result.code : ''}`;
                    byId('operation-result').textContent = JSON.stringify({ game: target, operation: operation.id, requestId: request.requestId, ...result }, null, 2);
                }
            } catch (error) {
                message.textContent = error.message;
                if (version === selectionVersion && authenticated) byId('operation-status').textContent = '結果を確認できません。操作は自動で再送されません。';
            } finally { submit.disabled = false; }
        });
        card.append(form);
        container.append(card);
    }
}

function renderEvents() {
    const query = byId('event-filter').value.toLocaleLowerCase();
    const kind = { metrics: 'metric', logs: 'log', traces: 'span' }[activeTab];
    const filtered = events.filter(item => (!kind || item.kind === kind) && JSON.stringify(item).toLocaleLowerCase().includes(query));
    const render = (container, items) => {
        container.replaceChildren();
        if (!items.length) { container.append(element('p', '該当するイベントはありません。', 'hint')); return; }
        const table = element('table');
        const head = element('thead'); const headings = element('tr');
        for (const title of ['種類', '名前', '値 / 要約', 'Trace ID']) headings.append(element('th', title));
        head.append(headings); table.append(head);
        const body = element('tbody');
        for (const item of items.slice().reverse()) {
            const row = element('tr');
            const nameCell = element('td'); const detail = element('button', item.name);
            detail.addEventListener('click', () => { byId('event-detail').textContent = JSON.stringify(item, null, 2); });
            nameCell.append(detail);
            row.append(element('td', item.kind), nameCell, element('td', valueOf(item.value)), element('td', item.traceId ?? '—'));
            body.append(row);
        }
        table.append(body); container.append(table);
    };
    render(byId('event-table'), filtered);
    render(byId('recent-events'), events.slice(-6));
}

function startStream() {
    stopStream();
    if (!authenticated || document.hidden) return;
    const target = selectedId;
    const version = selectionVersion;
    source = new EventSource('/api/ui/events' + (target ? '?sessionId=' + encodeURIComponent(target) : ''));
    const current = source;
    current.onopen = () => { if (source === current) byId('connection-status').textContent = '接続中'; };
    current.onerror = async () => {
        if (source !== current) return;
        byId('connection-status').textContent = '再接続待ち';
        try { const state = await api('/bootstrap'); csrf = state.requestToken; if (!state.authenticated) showLogin(); } catch (error) { byId('workspace-error').textContent = error.message; }
    };
    current.addEventListener('state', async event => {
        if (source !== current || version !== selectionVersion) return;
        try {
            const state = JSON.parse(event.data);
            const changed = state.sessions.map(item => item.sessionId).join(',') !== sessions.map(item => item.sessionId).join(',');
            if (changed) {
                await loadSessions();
                if (source !== current) return;
                startStream();
                return;
            }
            for (const summary of state.sessions) Object.assign(sessions.find(item => item.sessionId === summary.sessionId), summary);
            if (state.selectedSessionId === selectedId) events = state.events;
            else events = [];
            const session = selected();
            byId('received').textContent = session?.telemetryReceived ?? '0';
            byId('dropped').textContent = session?.telemetryDropped ?? '0';
            byId('pending').textContent = String(session?.pendingCommands ?? 0);
            byId('updated-at').textContent = '更新 ' + new Date().toLocaleTimeString();
            renderEvents();
        } catch (error) { byId('workspace-error').textContent = error.message; }
    });
}

byId('login-form').addEventListener('submit', async event => {
    event.preventDefault(); byId('login-error').textContent = '';
    const token = byId('operator-token').value; byId('operator-token').value = '';
    const button = document.querySelector("#login-form button"); button.disabled = true;
    try { await api('/login', { method: 'POST', body: new URLSearchParams({ token }) }); await bootstrap(); }
    catch (error) { byId('login-error').textContent = error.message; }
    finally { button.disabled = false; }
});
byId('logout').addEventListener('click', async () => {
    try { await api('/logout', { method: 'POST' }); showLogin(); await bootstrap(); }
    catch (error) { byId('workspace-error').textContent = error.message; }
});
byId('game-select').addEventListener('change', event => {
    selectedId = event.target.value; events = []; selectionVersion++;
    byId('event-detail').textContent = 'イベントを選択してください。';
    byId('operation-result').textContent = '';
    byId('operation-status').textContent = '操作を実行すると結果を表示します。';
    renderSession(); startStream();
});
byId('refresh').addEventListener('click', async () => {
    try { await loadSessions(); startStream(); byId('workspace-error').textContent = ''; }
    catch (error) { byId('workspace-error').textContent = error.message; }
});
byId('disconnect').addEventListener('click', async () => {
    const target = selectedId;
    if (!target || !confirm('このゲーム接続を終了します。接続に属するInputリースも解除されます。続行しますか？')) return;
    try { await api(`/sessions/${target}/connection`, { method: 'DELETE' }); await loadSessions(); startStream(); }
    catch (error) { byId('workspace-error').textContent = error.message; }
});
for (const tab of document.querySelectorAll('[data-tab]')) tab.addEventListener('click', () => {
    activeTab = tab.dataset.tab;
    for (const item of document.querySelectorAll('[data-tab]')) { item.removeAttribute('aria-current'); }
    tab.setAttribute('aria-current', 'page');
    byId('page-title').textContent = titles[activeTab];
    byId('overview-panel').hidden = activeTab !== 'overview';
    byId('operations-panel').hidden = activeTab !== 'operations';
    byId('input-panel').hidden = activeTab !== 'input';
    byId('telemetry-panel').hidden = !['metrics', 'logs', 'traces'].includes(activeTab);
    renderEvents();
});
byId('event-filter').addEventListener('input', renderEvents);
byId('operation-filter').addEventListener('input', renderSession);
byId('input-filter').addEventListener('input', renderSession);
document.addEventListener('visibilitychange', () => { if (document.hidden) stopStream(); else startStream(); });
window.addEventListener('pagehide', stopStream);
bootstrap().catch(error => { byId('login-error').textContent = error.message; });

import { scalarText, filterTelemetry, formatDuration, groupTraces, metricSeries, normalizeSamples, TelemetryViewState } from '/assets/telemetry-model.js';

const byId = id => document.getElementById(id);
const titles = { overview: 'Resources', operations: 'Operations', input: 'Input', metrics: 'Metrics', logs: 'Logs', traces: 'Traces' };
let csrf = '';
let authenticated = false;
let sessions = [];
let selectedId = '';
let events = [];
const telemetryView = new TelemetryViewState();
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
    resetTelemetry();
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
    applyRoute();
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
        const requested = route().sessionId || selectedId;
        const next = sessions.some(session => session.sessionId === requested) ? requested : route().sessionId ? '' : sessions[0]?.sessionId ?? '';
        if (sessions.length && !next) { const placeholder = element('option', '診断対象を選択してください'); placeholder.value = ''; select.prepend(placeholder); }
        if (next !== selectedId) {
            selectedId = next;
            resetTelemetry();
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
    renderResources();
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

function resetTelemetry() {
    telemetryView.reset();
    events = [];
    byId('pause-telemetry').textContent = '表示を一時停止';
    byId('pause-telemetry').setAttribute('aria-pressed', 'false');
    byId('telemetry-status').textContent = '選択したゲームの最新200件';
    byId('event-relations').replaceChildren();
}

function route() {
    const parameters = new URLSearchParams(location.hash.slice(1));
    const view = parameters.get('view');
    return { view: Object.hasOwn(titles, view) ? view : 'overview', sessionId: parameters.get('session') ?? '', traceId: parameters.get('trace') ?? '' };
}

function setView(view) {
    activeTab = view;
    for (const item of document.querySelectorAll('[data-tab]')) {
        if (item.dataset.tab === view) item.setAttribute('aria-current', 'page');
        else item.removeAttribute('aria-current');
    }
    byId('page-title').textContent = titles[view];
    byId('overview-panel').hidden = view !== 'overview';
    byId('operations-panel').hidden = view !== 'operations';
    byId('input-panel').hidden = view !== 'input';
    byId('telemetry-panel').hidden = !['metrics', 'logs', 'traces'].includes(view);
    byId('log-level').hidden = view !== 'logs';
    byId('log-level-label').hidden = view !== 'logs';
    byId('trace-filter').hidden = !['logs', 'traces'].includes(view);
    byId('trace-filter-label').hidden = !['logs', 'traces'].includes(view);
    byId('event-filter-label').textContent = view === 'metrics' ? 'メトリック名・タグで検索' : view === 'logs' ? 'Category・Message・フィールドで検索' : 'Operation・フィールドで検索';
    renderEvents();
}

function navigateTo(view, sessionId = selectedId, traceId = '') {
    const parameters = new URLSearchParams({ view });
    if (sessionId) parameters.set('session', sessionId);
    if (traceId) parameters.set('trace', traceId);
    location.hash = parameters.toString();
    applyRoute();
}

function applyRoute() {
    if (!authenticated) return;
    const requested = route();
    if (requested.sessionId && sessions.some(session => session.sessionId === requested.sessionId) && requested.sessionId !== selectedId) {
        selectedId = requested.sessionId;
        byId('game-select').value = selectedId;
        selectionVersion++;
        resetTelemetry();
        byId('event-detail').textContent = 'イベントを選択してください。';
        byId('operation-result').textContent = '';
        byId('operation-status').textContent = '操作を実行すると結果を表示します。';
        renderSession();
        startStream();
    }
    const missing = requested.sessionId && !sessions.some(session => session.sessionId === requested.sessionId);
    byId('workspace-error').textContent = missing ? 'リンク先のゲームは接続されていません。現在の診断対象を確認してください。' : '';
    byId('trace-filter').value = missing ? '' : requested.traceId;
    setView(requested.view);
}

function renderResources() {
    const container = byId('resource-table');
    container.replaceChildren();
    const query = byId('resource-filter').value.toLocaleLowerCase();
    const resources = sessions.filter(item => `${item.instanceId} ${item.sessionId} ${item.catalog.map(value => value.subsystem.displayName).join(' ')}`.toLocaleLowerCase().includes(query));
    if (!resources.length) { container.append(element('p', '接続中のゲームはありません。', 'hint')); return; }
    const table = element('table'); const head = element('tr');
    for (const title of ['Resource', '接続', '受信 / 欠落', '診断']) head.append(element('th', title));
    table.append(head);
    for (const resource of resources) {
        const row = element('tr');
        const name = element('td', `Game ${resource.instanceId.slice(0, 8)}`); name.title = resource.instanceId;
        const actions = element('td'); const links = element('div', undefined, 'resource-actions');
        for (const view of ['logs', 'traces', 'metrics', 'operations']) {
            const button = element('button', titles[view]); button.dataset.resource = resource.sessionId; button.dataset.view = view;
            button.addEventListener('click', () => navigateTo(view, resource.sessionId)); links.append(button);
        }
        actions.append(links);
        row.append(name, element('td', 'Connected', 'resource-state'), element('td', `${resource.telemetryReceived} / ${resource.telemetryDropped}`), actions);
        table.append(row);
    }
    container.append(table);
}

function inspectEvent(item) {
    byId('event-detail').textContent = JSON.stringify(item, null, 2);
    const relations = byId('event-relations'); relations.replaceChildren();
    if (item.traceId) {
        for (const view of ['logs', 'traces']) {
            const button = element('button', `関連${titles[view]}`); button.dataset.correlated = view;
            button.addEventListener('click', () => navigateTo(view, selectedId, item.traceId)); relations.append(button);
        }
    }
}

function renderEventRows(container, items, kind) {
    container.replaceChildren();
    if (!items.length) { container.append(element('p', '該当するイベントはありません。', 'hint')); return; }
    const table = element('table'); const head = element('tr');
    const headings = kind === 'log' ? ['レベル', 'Category', 'Message', 'Trace ID'] : kind === 'span' ? ['Span', 'Operation', '処理時間', 'Trace ID'] : ['種類', '名前', '値 / 要約', 'Trace ID'];
    for (const title of headings) head.append(element('th', title));
    table.append(head);
    const rows = kind === 'span' ? groupTraces(items).flatMap(group => group.spans.map(entry => ({ ...entry, longest: group.longest }))) : items.slice().reverse().map(span => ({ span }));
    for (const { span: item, depth = 0, parentMissing, longest } of rows) {
        const row = element('tr');
        const severity = scalarText(item.fields['log.level']) || 'Unknown';
        const prefix = kind === 'log' ? severity : kind === 'span' ? (parentMissing ? '親Span未保持' : depth === 0 ? 'Root' : `Child ${depth}`) : item.kind;
        const nameCell = element('td'); const detail = element('button', (kind === 'span' ? '↳ '.repeat(Math.min(depth, 8)) : '') + item.name);
        detail.addEventListener('click', () => inspectEvent(item)); nameCell.append(detail);
        const valueCell = element('td', kind === 'span' ? formatDuration(item.durationTicks) : scalarText(item.value));
        if (kind === 'span') {
            const bar = element('progress', undefined, 'span-duration'); bar.max = 10000;
            bar.value = longest > 0n ? Number(BigInt(item.durationTicks) * 10000n / longest) : 0;
            bar.setAttribute('aria-label', `${item.name} ${formatDuration(item.durationTicks)}`); valueCell.append(bar);
        }
        const correlation = element('td');
        if (item.traceId) {
            const link = element('button', item.traceId, 'trace-id'); link.dataset.traceLink = item.traceId;
            link.addEventListener('click', () => navigateTo('traces', selectedId, item.traceId)); correlation.append(link);
        } else correlation.textContent = '—';
        row.append(element('td', prefix, kind === 'log' ? 'severity-' + severity.toLocaleLowerCase() : ''), nameCell, valueCell, correlation);
        table.append(row);
    }
    container.append(table);
}

function renderTraceSummary(items) {
    const container = byId('trace-summary'); container.replaceChildren();
    if (activeTab !== 'traces') return;
    for (const group of groupTraces(items)) {
        const card = element('article', undefined, 'card trace-card');
        const title = element('button', group.name); title.addEventListener('click', () => navigateTo('traces', selectedId, group.traceId));
        const logs = element('button', '関連Logs'); logs.dataset.correlated = 'logs'; logs.addEventListener('click', () => navigateTo('logs', selectedId, group.traceId));
        card.append(title, element('span', `${group.spans.length} spans · ${group.hasError ? 'Error' : '保持SpanにErrorなし'} · 最長Span ${formatDuration(group.longest)}`), element('span', group.traceId, 'trace-id'), logs);
        container.append(card);
    }
    if (container.childElementCount) container.append(element('p', '保持中のSpanの親子関係と処理時間を表示します。バーは最長Spanとの比率です。', 'hint'));
}

function renderMetrics(items) {
    const container = byId('metric-charts'); container.replaceChildren();
    if (activeTab !== 'metrics') return;
    const series = metricSeries(items);
    for (const group of series.slice(0, 20)) {
        const card = element('article', undefined, 'card');
        card.append(element('h2', group.name), element('p', scalarText(group.latest.value), 'metric-value'), element('p', `保持中の観測値 ${group.samples.length} 件（受信順）`, 'hint'));
        const samples = normalizeSamples(group.samples);
        if (samples.length) {
            const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg'); svg.setAttribute('viewBox', '0 0 300 80'); svg.setAttribute('class', 'metric-chart'); svg.setAttribute('role', 'img'); svg.setAttribute('aria-label', `${group.name} 受信順の観測値`);
            const points = samples.map((value, index) => `${samples.length === 1 ? 150 : index * 300 / (samples.length - 1)},${72 - value * 64}`);
            const line = document.createElementNS(svg.namespaceURI, 'polyline'); line.setAttribute('points', points.join(' ')); svg.append(line);
            if (samples.length === 1) { const dot = document.createElementNS(svg.namespaceURI, 'circle'); dot.setAttribute('cx', '150'); dot.setAttribute('cy', '40'); dot.setAttribute('r', '3'); svg.append(dot); }
            card.append(svg);
        }
        card.append(element('p', group.tags.map(([key, value]) => `${key}=${scalarText(value)}`).join(', '), 'hint'));
        container.append(card);
    }
    if (series.length > 20) container.append(element('p', 'グラフは先頭20系列です。検索で絞り込めます。', 'hint'));
}

function renderEvents() {
    const kind = { metrics: 'metric', logs: 'log', traces: 'span' }[activeTab];
    const filtered = filterTelemetry(events, { kind, query: byId('event-filter').value, traceId: kind === 'metric' ? '' : byId('trace-filter').value.trim(), minimumLevel: byId('log-level').value });
    renderTraceSummary(filtered);
    renderMetrics(filtered);
    renderEventRows(byId('event-table'), filtered, kind);
    renderEventRows(byId('recent-events'), events.slice(-6));
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
            telemetryView.update(state.selectedSessionId === selectedId ? state.events : []);
            events = telemetryView.displayed;
            const session = selected();
            byId('received').textContent = session?.telemetryReceived ?? '0';
            byId('dropped').textContent = session?.telemetryDropped ?? '0';
            byId('pending').textContent = String(session?.pendingCommands ?? 0);
            byId('updated-at').textContent = '更新 ' + new Date().toLocaleTimeString();
            renderResources();
            if (!telemetryView.paused) renderEvents();
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
byId('game-select').addEventListener('change', event => navigateTo(activeTab, event.target.value));
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
for (const tab of document.querySelectorAll('[data-tab]')) tab.addEventListener('click', () => navigateTo(tab.dataset.tab));
byId('resource-filter').addEventListener('input', renderResources);
byId('trace-filter').addEventListener('input', renderEvents);
byId('log-level').addEventListener('change', renderEvents);
byId('pause-telemetry').addEventListener('click', () => {
    telemetryView.setPaused(!telemetryView.paused);
    events = telemetryView.displayed;
    byId('pause-telemetry').textContent = telemetryView.paused ? '表示を再開' : '表示を一時停止';
    byId('pause-telemetry').setAttribute('aria-pressed', String(telemetryView.paused));
    byId('telemetry-status').textContent = telemetryView.paused ? '表示を一時停止しています。収集は継続します。' : '選択したゲームの最新200件';
    renderEvents();
});
byId('clear-telemetry-filters').addEventListener('click', () => {
    byId('event-filter').value = ''; byId('trace-filter').value = ''; byId('log-level').value = '';
    navigateTo(activeTab);
});
window.addEventListener('hashchange', applyRoute);
byId('event-filter').addEventListener('input', renderEvents);
byId('operation-filter').addEventListener('input', renderSession);
byId('input-filter').addEventListener('input', renderSession);
document.addEventListener('visibilitychange', () => { if (document.hidden) stopStream(); else startStream(); });
window.addEventListener('pagehide', stopStream);
bootstrap().catch(error => { byId('login-error').textContent = error.message; });

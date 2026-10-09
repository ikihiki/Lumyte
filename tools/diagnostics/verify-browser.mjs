import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomBytes, randomUUID } from 'node:crypto';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';

const root = fileURLToPath(new URL('../../', import.meta.url));
const dotnet = process.env.DOTNET ?? 'dotnet';
const chrome = process.env.CHROME ?? 'chromium';
const output = resolve(root, process.argv[2] ?? 'artifacts/test-results/diagnostics-ui');
const serverDll = resolve(root, 'artifacts/diagnostics-ui-server/Lumyte.Diagnostics.Server.dll');
const gameDll = resolve(root, 'samples/Lumyte.Diagnostics.Remote.Sample/bin/Release/net10.0/Lumyte.Diagnostics.Remote.Sample.dll');
await mkdir(output, { recursive: true });
const profile = await mkdtemp(resolve(tmpdir(), 'lumyte-diagnostics-ui-'));
const gameToken = randomBytes(32).toString('hex');
const operatorToken = randomBytes(32).toString('hex');
async function freePort() {
    const listener = createServer();
    await new Promise((resolve, reject) => { listener.once('error', reject); listener.listen(0, '127.0.0.1', resolve); });
    const port = listener.address().port;
    await new Promise(resolve => listener.close(resolve));
    return port;
}
const httpPort = await freePort();
let grpcPort = await freePort();
while (grpcPort === httpPort) grpcPort = await freePort();
const origin = `http://127.0.0.1:${httpPort}`;
const env = { ...process.env, LUMYTE_DIAGNOSTICS_GAME_TOKEN: gameToken, LUMYTE_DIAGNOSTICS_OPERATOR_TOKEN: operatorToken, LUMYTE_DIAGNOSTICS_HTTP_PORT: String(httpPort), LUMYTE_DIAGNOSTICS_GRPC_PORT: String(grpcPort) };
const children = [];
const logs = new Map();
function launch(name, executable, args, cwd, environment = env) {
    const child = spawn(executable, args, { cwd, env: environment, stdio: ['ignore', 'pipe', 'pipe'] });
    children.push(child); logs.set(name, '');
    const append = data => logs.set(name, logs.get(name) + data);
    child.stdout.on('data', append); child.stderr.on('data', append);
    child.on('error', error => append(String(error)));
    return child;
}
async function waitFor(condition, description, milliseconds = 15000) {
    const deadline = Date.now() + milliseconds;
    while (Date.now() < deadline) { if (await condition()) return; await delay(100); }
    throw new Error(`Timed out: ${description}`);
}
let socket;
let captureFailure = async () => {};
const errors = [];
const results = [];
try {
    await readFile(serverDll);
    await readFile(gameDll);
    // Start the published server outside the source tree to verify published assets.
    launch('server', dotnet, [serverDll], profile);
    await waitFor(async () => { try { return (await fetch(origin + '/')).ok; } catch { return false; } }, 'published server');
    launch('browser', chrome, ['--headless', '--no-sandbox', '--disable-dev-shm-usage', '--remote-debugging-port=0', `--user-data-dir=${resolve(profile, 'chrome')}`, 'about:blank'], profile, process.env);
    let debuggerPort;
    await waitFor(() => { debuggerPort = /DevTools listening on ws:\/\/127\.0\.0\.1:(\d+)/.exec(logs.get('browser'))?.[1]; return debuggerPort; }, 'Chromium');
    const pages = await (await fetch(`http://127.0.0.1:${debuggerPort}/json/list`)).json();
    socket = new WebSocket(pages.find(page => page.type === 'page').webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
    let nextId = 0;
    let navigationCount = 0;
    let signalRConnection = false;
    let legacyStream = false;
    const pending = new Map();
    socket.addEventListener('message', event => {
        const message = JSON.parse(event.data);
        if (message.id) {
            const action = pending.get(message.id); pending.delete(message.id);
            if (action) { if (message.error) action.reject(new Error(JSON.stringify(message.error))); else action.resolve(message.result); }
        } else if (message.method === 'Page.frameNavigated' && !message.params.frame.parentId) {
            navigationCount++;
        } else if (message.method === 'Network.webSocketCreated' && message.params.url.includes('/_blazor')) {
            signalRConnection = true;
        } else if (message.method === 'Network.requestWillBeSent' && message.params.request.url.includes('/api/ui/events')) {
            legacyStream = true;
        } else if (message.method === 'Runtime.exceptionThrown') {
            errors.push(message.params.exceptionDetails.exception?.description ?? message.params.exceptionDetails.text);
        } else if (message.method === 'Runtime.consoleAPICalled' && message.params.type === 'error') {
            errors.push(message.params.args.map(value => value.description ?? String(value.value)).join(' '));
        } else if (message.method === 'Page.javascriptDialogOpening') {
            command('Page.handleJavaScriptDialog', { accept: true }).catch(error => errors.push(error.message));
        }
    });
    function command(method, params = {}, sessionId) {
        const id = ++nextId;
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 10000);
            pending.set(id, { resolve: value => { clearTimeout(timeout); resolve(value); }, reject: error => { clearTimeout(timeout); reject(error); } });
            socket.send(JSON.stringify({ id, method, params, sessionId }));
        });
    }
    async function evaluate(expression, sessionId) {
        const result = await command('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true }, sessionId);
        if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
        return result.result.value;
    }
    captureFailure = async () => {
        const html = await evaluate("{ const copy = document.documentElement.cloneNode(true); for (const input of copy.querySelectorAll('input')) input.setAttribute('value', '[redacted]'); copy.outerHTML; }");
        await writeFile(resolve(output, 'failure.html'), String(html).replaceAll(gameToken, '[redacted]').replaceAll(operatorToken, '[redacted]'));
        await command('Page.captureScreenshot').then(result => writeFile(resolve(output, 'failure.png'), Buffer.from(result.data, 'base64')));
    };
    async function reload() {
        const previous = navigationCount;
        await command('Page.reload');
        await waitFor(() => navigationCount > previous, 'reloaded document');
    }
    await command('Runtime.enable'); await command('Page.enable'); await command('Network.enable');
    await command('Emulation.setDeviceMetricsOverride', { width: 1440, height: 960, deviceScaleFactor: 1, mobile: false });
    await command('Page.navigate', { url: origin });
    await waitFor(() => evaluate("Boolean(document.querySelector('#login-form button:not(:disabled)'))"), 'login form');
    await evaluate(`document.getElementById('operator-token').value = ${JSON.stringify(operatorToken)}; document.getElementById('login-form').requestSubmit();`);
    await waitFor(() => evaluate("Boolean(document.getElementById('workspace')) && document.getElementById('workspace').hidden === false"), 'cookie login');
    assert.equal(await evaluate("document.getElementById('operator-token') === null"), true);
    assert.equal(await evaluate('localStorage.length + sessionStorage.length'), 0);
    assert.ok(!(await evaluate('document.cookie')).includes('Lumyte.Diagnostics.Operator'));
    await reload();
    await waitFor(() => evaluate("Boolean(document.getElementById('workspace')) && document.getElementById('workspace').hidden === false"), 'cookie login after reload');
    for (const transport of ['http', 'magiconion']) {
        const endpoint = transport === 'http' ? httpPort : grpcPort;
        const game = launch(transport, dotnet, [gameDll, transport, `http://127.0.0.1:${endpoint}`, '60'], root);
        await waitFor(() => evaluate("Array.from(document.querySelectorAll('#game-select option')).filter(option => option.value.length === 36).length === 1"), `${transport} catalog`);
        await evaluate("{ const choice = Array.from(document.querySelectorAll('#game-select option')).find(option => option.value.length === 36); document.getElementById('game-select').value = choice.value; document.getElementById('game-select').dispatchEvent(new Event('change', { bubbles: true })); document.querySelector('[data-tab=input]').click(); }");
        await waitFor(() => evaluate("Boolean(document.querySelector('#input-forms form'))"), 'generated Input form');
        await evaluate(`{ const form = document.querySelector('#input-forms form'); form.elements.namedItem('button').value = 'Jump'; form.elements.namedItem('pressed').value = 'true'; form.elements.namedItem('duration-ms').value = '5000'; for (const input of form.querySelectorAll('input,select')) input.dispatchEvent(new Event('change', { bubbles: true })); form.requestSubmit(); }`);
        await waitFor(() => evaluate("document.getElementById('operation-result').textContent.includes('\"status\": \"success\"')"), `${transport} operation result`);
        await waitFor(() => logs.get(transport).includes('Input state: Jump=True'), `${transport} input change`);
        await evaluate("document.querySelector('[data-tab=metrics]').click()");
        await waitFor(() => evaluate("document.getElementById('event-table')?.textContent.includes('9007199254740993')"), 'lossless Int64 Metric');
        await evaluate("document.querySelector('[data-tab=logs]').click()");
        await waitFor(() => evaluate("document.getElementById('event-table')?.textContent.includes('Game instance connected')"), 'Log display');
        await evaluate("document.querySelector('#event-table td fluent-button').click()");
        await waitFor(() => evaluate("document.getElementById('event-detail').textContent.includes('\"kind\": \"log\"')"), 'log detail');
        const log = JSON.parse(await evaluate("document.getElementById('event-detail').textContent"));
        await evaluate("document.querySelector('[data-tab=traces]').click()");
        await waitFor(() => evaluate("document.getElementById('event-table')?.textContent.includes('remote.start')"), 'Trace display');
        await evaluate("document.querySelector('#event-table td fluent-button').click()");
        await waitFor(() => evaluate("document.getElementById('event-detail').textContent.includes('\"kind\": \"span\"')"), 'span detail');
        const trace = JSON.parse(await evaluate("document.getElementById('event-detail').textContent"));
        assert.equal(trace.traceId, log.traceId);
        await command('Page.captureScreenshot').then(result => writeFile(resolve(output, `${transport}-desktop.png`), Buffer.from(result.data, 'base64')));
        await command('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
        assert.equal(await evaluate('document.documentElement.scrollWidth <= window.innerWidth'), true, 'mobile overflow');
        await command('Emulation.setDeviceMetricsOverride', { width: 1440, height: 960, deviceScaleFactor: 1, mobile: false });
        await evaluate("document.getElementById('disconnect').click()");
        await waitFor(() => evaluate("Boolean(document.getElementById('confirm-disconnect'))"), 'disconnect confirmation');
        await evaluate("document.getElementById('confirm-disconnect').click()");
        await waitFor(() => game.exitCode !== null, `${transport} disconnect`);
        assert.equal(game.exitCode, 0);
        assert.ok(logs.get(transport).includes('Disconnected: Jump=False'));
        await waitFor(() => evaluate("document.getElementById('disconnect').disabled"), 'removed game');
        results.push({ transport, publishedServer: true, cookieLogin: true, generatedOperation: true, inputChanged: true, metricInt64: '9007199254740993', traceLogCorrelated: true, disconnectReleasedInput: true, mobileOverflow: false });
    }
    // Controlled resources exercise the resource-oriented query and navigation model.
    async function gameRequest(method, path, body, secret) {
        const headers = { Authorization: 'Bearer ' + gameToken };
        if (secret) headers['X-Diagnostics-Session'] = secret;
        if (body) headers['Content-Type'] = 'application/json';
        const response = await fetch(origin + '/diagnostics/v1/' + path, { method, headers, body: body ? JSON.stringify(body) : undefined });
        assert.ok(response.ok, `Probe request: HTTP ${response.status}`);
        return response.status === 204 ? null : response.json();
    }
    const catalog = [{ subsystem: { id: 'probe', displayName: 'Probe', schemaVersion: 1 }, operations: [] }];
    const first = await gameRequest('POST', 'sessions', { instanceId: randomUUID(), protocolVersion: 1, catalog });
    const second = await gameRequest('POST', 'sessions', { instanceId: randomUUID(), protocolVersion: 1, catalog });
    const traceId = '1234567890abcdef1234567890abcdef';
    const zero = '0000000000000000';
    const scalar = string => ({ kind: 3, string });
    const log = (message, level, trace = traceId) => ({ kind: 'log', timestamp: '1', name: 'Probe.Category', value: scalar(message), traceId: trace, spanId: '1111111111111111', parentSpanId: null, durationTicks: '0', fields: { 'log.level': scalar(level) } });
    const span = (name, id, parent, duration) => ({ kind: 'span', timestamp: '2', name, value: scalar('Unset'), traceId, spanId: id, parentSpanId: parent, durationTicks: duration, fields: {} });
    const publish = (session, events) => gameRequest('POST', `sessions/${session.sessionId}/messages`, { messageId: randomUUID(), sessionId: session.sessionId, kind: 1, requestId: null, result: null, events }, session.sessionSecret);
    const metric = value => ({ kind: 'metric', timestamp: value, name: 'probe.precision', value: { kind: 1, int64: value }, traceId: null, spanId: null, parentSpanId: null, durationTicks: '0', fields: {} });
    await publish(first, [log('information', 'Information'), log('failure', 'Error'), log('<img src=x onerror="window.__lumyteProbe=true">', 'Warning'), log('wrong-trace', 'Error', 'abcdef1234567890abcdef1234567890'), span('parent', '1111111111111111', zero, '50000'), span('child', '2222222222222222', '1111111111111111', '10000'), ...['9223372036854775805', '9223372036854775806', '9223372036854775807'].map(metric)]);
    await publish(second, [log('other-resource', 'Error')]);
    await evaluate("document.querySelector('[data-tab=overview]').click()");
    await waitFor(() => evaluate("document.querySelectorAll('#resource-table fluent-button[data-view=logs]').length === 2"), 'resource table');
    await evaluate(`document.querySelector('#resource-table fluent-button[data-resource="${first.sessionId}"][data-view="logs"]').click()`);
    await waitFor(() => evaluate("document.getElementById('event-table')?.textContent.includes('information')"), 'resource log navigation');
    assert.ok(!(await evaluate("document.getElementById('event-table').textContent")).includes('other-resource'));
    assert.ok((await evaluate("document.getElementById('event-table').textContent")).includes('<img'));
    assert.equal(await evaluate("document.querySelector('img') === null && typeof window.__lumyteProbe === 'undefined'"), true, 'untrusted log text');
    await evaluate("document.getElementById('log-level').value = 'Error'; document.getElementById('log-level').dispatchEvent(new Event('change', { bubbles: true }));");
    await waitFor(async () => !(await evaluate("document.getElementById('event-table').textContent")).includes('information'), 'severity filter');
    await evaluate("document.getElementById('pause-telemetry').click()");
    await waitFor(() => evaluate("document.getElementById('pause-telemetry').getAttribute('aria-pressed') === 'true'"), 'paused view');
    await publish(first, [log('later-error', 'Error')]);
    await waitFor(() => evaluate("document.getElementById('received').textContent === '10'"), 'collect while paused');
    assert.ok(!(await evaluate("document.getElementById('event-table').textContent")).includes('later-error'));
    await evaluate("document.getElementById('pause-telemetry').click()");
    await waitFor(() => evaluate("document.getElementById('event-table')?.textContent.includes('later-error')"), 'resume newest snapshot');
    await evaluate("document.querySelector('#event-table td fluent-button').click()");
    await waitFor(() => evaluate("Boolean(document.querySelector('#event-relations fluent-button[data-correlated=traces]'))"), 'correlation links');
    await evaluate("document.querySelector('#event-relations fluent-button[data-correlated=traces]').click()");
    await waitFor(() => evaluate("document.getElementById('event-table')?.textContent.includes('Child 1')"), 'span hierarchy');
    assert.equal(await evaluate("document.getElementById('trace-filter').value"), traceId);
    await reload();
    await waitFor(() => evaluate("Boolean(document.getElementById('event-table')) && document.getElementById('event-table')?.textContent.includes('Child 1')"), 'trace deep link after reload');
    await evaluate("document.querySelector('#trace-summary fluent-button[data-correlated=logs]').click()");
    await waitFor(() => evaluate("document.getElementById('page-title')?.textContent === 'Logs' && document.getElementById('event-table')?.textContent.includes('failure')"), 'Trace to Logs');
    assert.ok(!(await evaluate("document.getElementById('event-table').textContent")).includes('wrong-trace'));
    await evaluate("document.querySelector('[data-tab=metrics]').click()");
    await waitFor(() => evaluate("document.getElementById('metric-charts')?.textContent.includes('9223372036854775807')"), 'metric series chart');
    const points = await evaluate("document.querySelector('#metric-charts polyline').getAttribute('points')");
    assert.equal(points, '0,72 150,40 300,8');
    await command('Page.captureScreenshot').then(result => writeFile(resolve(output, 'aspire-metrics-desktop.png'), Buffer.from(result.data, 'base64')));
    await evaluate(`document.getElementById('game-select').value = '${second.sessionId}'; document.getElementById('game-select').dispatchEvent(new Event('change', { bubbles: true })); document.querySelector('[data-tab=logs]').click();`);
    await waitFor(() => evaluate("document.getElementById('event-table')?.textContent.includes('other-resource')"), 'switch resource');
    assert.ok(!(await evaluate("document.getElementById('event-table').textContent")).includes('later-error'));
    await gameRequest('DELETE', `sessions/${first.sessionId}`, undefined, first.sessionSecret);
    await gameRequest('DELETE', `sessions/${second.sessionId}`, undefined, second.sessionSecret);
    results.push({ scenario: 'aspire-dashboard-patterns', resourceNavigation: true, severityFilter: true, pauseResume: true, traceDeepLink: true, spanHierarchy: true, traceToLogs: true, metricSeriesChart: true, int64ChartPrecision: true, resourceIsolation: true, untrustedText: true });
    assert.equal(signalRConnection, true, 'Blazor SignalR WebSocket');
    assert.equal(legacyStream, false, 'legacy SSE removed');
    assert.equal(await evaluate("document.querySelectorAll('fluent-button').length > 0"), true, 'Fluent UI components');
    const otherPage = await command('Target.createTarget', { url: origin });
    const attached = await command('Target.attachToTarget', { targetId: otherPage.targetId, flatten: true });
    await waitFor(() => evaluate("document.getElementById('workspace')?.getAttribute('data-ready') === 'true'", attached.sessionId), 'second authenticated Circuit');
    await evaluate("document.getElementById('logout').click()");
    await waitFor(() => evaluate("Boolean(document.getElementById('login-form'))"), 'logout');
    assert.equal(await evaluate("fetch('/_blazor/negotiate?negotiateVersion=1', { method: 'POST' }).then(response => response.status)"), 401);
    assert.equal(await evaluate("document.getElementById('session-id') === null"), true);
    assert.equal(await evaluate("document.getElementById('operation-result') === null"), true);
    await waitFor(() => evaluate("Boolean(document.getElementById('expired')) && document.getElementById('workspace').hidden", attached.sessionId), 'logout revokes other Circuit');
    assert.equal(await evaluate("document.getElementById('operation-result').textContent", attached.sessionId), '');
    await command('Target.closeTarget', { targetId: otherPage.targetId });
    results.push({ scenario: 'blazor-server', signalRWebSocket: true, fluentUiComponents: true, noLegacySse: true, logoutRevokesOtherCircuit: true });
    assert.deepEqual(errors, [], 'browser errors');
    await writeFile(resolve(output, 'results.json'), JSON.stringify(results, null, 2) + '\n');
    console.log(JSON.stringify({ results, browserErrors: errors.length }));
} catch (error) {
    await captureFailure().catch(() => {});
    await writeFile(resolve(output, 'browser-errors.json'), JSON.stringify(errors, null, 2));
    throw error;
} finally {
    socket?.close();
    for (const child of children.reverse()) {
        if (child.exitCode === null && child.signalCode === null) {
            const stopped = new Promise(resolve => child.once('exit', resolve));
            const timeout = setTimeout(() => child.kill('SIGKILL'), 2000);
            child.kill('SIGTERM'); await stopped; clearTimeout(timeout);
        }
    }
    for (const [name, text] of logs) await writeFile(resolve(output, `${name}.log`), text.replaceAll(gameToken, '[redacted]').replaceAll(operatorToken, '[redacted]'));
    await rm(profile, { recursive: true, force: true, maxRetries: 3 });
}

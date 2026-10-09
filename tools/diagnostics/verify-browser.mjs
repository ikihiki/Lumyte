import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
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
const errors = [];
const results = [];
try {
    await readFile(serverDll);
    await readFile(gameDll);
    // Start the published server outside the source tree to verify embedded assets.
    launch('server', dotnet, [serverDll], profile);
    await waitFor(async () => { try { return (await fetch(origin + '/')).ok; } catch { return false; } }, 'published server');
    launch('browser', chrome, ['--headless', '--no-sandbox', '--disable-dev-shm-usage', '--remote-debugging-port=0', `--user-data-dir=${resolve(profile, 'chrome')}`, 'about:blank'], profile, process.env);
    let debuggerPort;
    await waitFor(() => { debuggerPort = /DevTools listening on ws:\/\/127\.0\.0\.1:(\d+)/.exec(logs.get('browser'))?.[1]; return debuggerPort; }, 'Chromium');
    const pages = await (await fetch(`http://127.0.0.1:${debuggerPort}/json/list`)).json();
    socket = new WebSocket(pages.find(page => page.type === 'page').webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
    let nextId = 0;
    const pending = new Map();
    socket.addEventListener('message', event => {
        const message = JSON.parse(event.data);
        if (message.id) {
            const action = pending.get(message.id); pending.delete(message.id);
            if (action) { if (message.error) action.reject(new Error(JSON.stringify(message.error))); else action.resolve(message.result); }
        } else if (message.method === 'Runtime.exceptionThrown') {
            errors.push(message.params.exceptionDetails.exception?.description ?? message.params.exceptionDetails.text);
        } else if (message.method === 'Runtime.consoleAPICalled' && message.params.type === 'error') {
            errors.push(message.params.args.map(value => value.description ?? String(value.value)).join(' '));
        } else if (message.method === 'Page.javascriptDialogOpening') {
            command('Page.handleJavaScriptDialog', { accept: true }).catch(error => errors.push(error.message));
        }
    });
    function command(method, params = {}) {
        const id = ++nextId;
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 10000);
            pending.set(id, { resolve: value => { clearTimeout(timeout); resolve(value); }, reject: error => { clearTimeout(timeout); reject(error); } });
            socket.send(JSON.stringify({ id, method, params }));
        });
    }
    async function evaluate(expression) {
        const result = await command('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
        if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
        return result.result.value;
    }
    await command('Runtime.enable'); await command('Page.enable');
    await command('Emulation.setDeviceMetricsOverride', { width: 1440, height: 960, deviceScaleFactor: 1, mobile: false });
    await command('Page.navigate', { url: origin });
    await waitFor(() => evaluate("Boolean(document.querySelector('#login-form button:not(:disabled)'))"), 'login form');
    await evaluate(`document.getElementById('operator-token').value = ${JSON.stringify(operatorToken)}; document.getElementById('login-form').requestSubmit();`);
    await waitFor(() => evaluate("document.getElementById('workspace').hidden === false"), 'cookie login');
    assert.equal(await evaluate("document.getElementById('operator-token').value"), '');
    assert.equal(await evaluate('localStorage.length + sessionStorage.length'), 0);
    assert.ok(!(await evaluate('document.cookie')).includes('Lumyte.Diagnostics.Operator'));
    await command('Page.reload');
    await waitFor(() => evaluate("Boolean(document.getElementById('workspace')) && document.getElementById('workspace').hidden === false"), 'cookie login after reload');
    for (const transport of ['http', 'magiconion']) {
        const endpoint = transport === 'http' ? httpPort : grpcPort;
        const game = launch(transport, dotnet, [gameDll, transport, `http://127.0.0.1:${endpoint}`, '60'], root);
        await waitFor(() => evaluate("document.querySelectorAll('#game-select option[value]').length === 1"), `${transport} catalog`);
        await evaluate("document.querySelector('[data-tab=input]').click()");
        await waitFor(() => evaluate("Boolean(document.querySelector('#input-forms form'))"), 'generated Input form');
        await evaluate(`{ const form = document.querySelector('#input-forms form'); form.elements.namedItem('button').value = 'Jump'; form.elements.namedItem('pressed').value = 'true'; form.elements.namedItem('duration-ms').value = '5000'; form.requestSubmit(); }`);
        await waitFor(() => evaluate("document.getElementById('operation-result').textContent.includes('\"status\": \"success\"')"), `${transport} operation result`);
        await waitFor(() => logs.get(transport).includes('Input state: Jump=True'), `${transport} input change`);
        await evaluate("document.querySelector('[data-tab=metrics]').click()");
        await waitFor(() => evaluate("document.getElementById('event-table').textContent.includes('9007199254740993')"), 'lossless Int64 Metric');
        await evaluate("document.querySelector('[data-tab=logs]').click()");
        await waitFor(() => evaluate("document.getElementById('event-table').textContent.includes('Game instance connected')"), 'Log display');
        await evaluate("document.querySelector('#event-table td button').click()");
        const log = JSON.parse(await evaluate("document.getElementById('event-detail').textContent"));
        await evaluate("document.querySelector('[data-tab=traces]').click()");
        await waitFor(() => evaluate("document.getElementById('event-table').textContent.includes('remote.start')"), 'Trace display');
        await evaluate("document.querySelector('#event-table td button').click()");
        const trace = JSON.parse(await evaluate("document.getElementById('event-detail').textContent"));
        assert.equal(trace.traceId, log.traceId);
        await command('Page.captureScreenshot').then(result => writeFile(resolve(output, `${transport}-desktop.png`), Buffer.from(result.data, 'base64')));
        await command('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
        assert.equal(await evaluate('document.documentElement.scrollWidth <= window.innerWidth'), true, 'mobile overflow');
        await command('Emulation.setDeviceMetricsOverride', { width: 1440, height: 960, deviceScaleFactor: 1, mobile: false });
        await evaluate("document.getElementById('disconnect').click()");
        await waitFor(() => game.exitCode !== null, `${transport} disconnect`);
        assert.equal(game.exitCode, 0);
        assert.ok(logs.get(transport).includes('Disconnected: Jump=False'));
        await waitFor(() => evaluate("document.getElementById('disconnect').disabled"), 'removed game');
        results.push({ transport, publishedServer: true, cookieLogin: true, generatedOperation: true, inputChanged: true, metricInt64: '9007199254740993', traceLogCorrelated: true, disconnectReleasedInput: true, mobileOverflow: false });
    }
    await evaluate("document.getElementById('logout').click()");
    await waitFor(() => evaluate("document.getElementById('workspace').hidden"), 'logout');
    assert.equal(await evaluate("fetch('/api/ui/sessions').then(response => response.status)"), 401);
    assert.equal(await evaluate("document.getElementById('session-id').textContent"), '—');
    assert.equal(await evaluate("document.getElementById('operation-result').textContent"), '');
    assert.deepEqual(errors, [], 'browser errors');
    await writeFile(resolve(output, 'results.json'), JSON.stringify(results, null, 2) + '\n');
    console.log(JSON.stringify({ results, browserErrors: errors.length }));
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

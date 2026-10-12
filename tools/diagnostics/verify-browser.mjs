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
await rm(resolve(output, 'gallery.json'), { force: true });
await rm(resolve(output, 'screenshots'), { recursive: true, force: true });
await mkdir(resolve(output, 'screenshots'), { recursive: true });
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
const screenshots = [];
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
    async function capture(id, title, description, page, viewport = 'desktop', fullPage = false) {
        assert.ok(!screenshots.some(item => item.id === id), `Duplicate gallery screenshot: ${id}`);
        await evaluate("document.fonts.ready.then(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))");
        const parameters = { captureBeyondViewport: fullPage };
        if (fullPage) {
            const { cssContentSize } = await command('Page.getLayoutMetrics');
            parameters.clip = { x: 0, y: 0, width: cssContentSize.width, height: cssContentSize.height, scale: 1 };
        }
        const file = `screenshots/${id}.png`;
        const image = await command('Page.captureScreenshot', parameters);
        await writeFile(resolve(output, file), Buffer.from(image.data, 'base64'));
        screenshots.push({ id, title, description, page, viewport, file });
    }
    captureFailure = async () => {
        const html = await evaluate("{ const copy = document.documentElement.cloneNode(true); for (const input of copy.querySelectorAll('input')) input.setAttribute('value', '[redacted]'); copy.outerHTML; }");
        await writeFile(resolve(output, 'failure.html'), String(html).replaceAll(gameToken, '[redacted]').replaceAll(operatorToken, '[redacted]'));
        await command('Page.captureScreenshot').then(result => writeFile(resolve(output, 'screenshots/failure.png'), Buffer.from(result.data, 'base64')));
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
    await capture('login', 'ログイン', '診断サーバーが配信するログイン画面です。操作用トークンを入力する前の状態を撮影しています。', 'login');
    await evaluate(`document.getElementById('operator-token').value = ${JSON.stringify(operatorToken)}; document.getElementById('login-form').requestSubmit();`);
    await waitFor(() => evaluate("Boolean(document.getElementById('workspace')) && document.getElementById('workspace').hidden === false"), 'cookie login');
    assert.equal(await evaluate("document.getElementById('operator-token') === null"), true);
    assert.equal(await evaluate('localStorage.length + sessionStorage.length'), 0);
    assert.ok(!(await evaluate('document.cookie')).includes('Lumyte.Diagnostics.Operator'));
    await reload();
    await waitFor(() => evaluate("Boolean(document.getElementById('workspace')) && document.getElementById('workspace').hidden === false"), 'cookie login after reload');
    for (const transport of ['http', 'magiconion']) {
        const endpoint = transport === 'http' ? httpPort : grpcPort;
        const game = launch(transport, dotnet, [gameDll, transport, `http://127.0.0.1:${endpoint}`, '120'], root, { ...env, LUMYTE_DIAGNOSTICS_SETTINGS_PATH: resolve(profile, `${transport}-settings.json`), LUMYTE_DIAGNOSTICS_RENDER_GRAPH: '1', LUMYTE_DIAGNOSTICS_ANIMATION: '1' });
        await waitFor(() => evaluate("Array.from(document.querySelectorAll('#game-select option')).filter(option => option.value.length === 36).length === 1"), `${transport} catalog`);
        await evaluate("{ const choice = Array.from(document.querySelectorAll('#game-select option')).find(option => option.value.length === 36); document.getElementById('game-select').value = choice.value; document.getElementById('game-select').dispatchEvent(new Event('change', { bubbles: true })); }");
        await waitFor(() => evaluate("document.querySelector('[data-tab=input]')?.disabled === false"), 'selected game navigation');
        if (transport === 'http') {
            await evaluate("document.querySelector('[data-tab=resources]').click()");
            await waitFor(() => evaluate("document.querySelectorAll('#resource-table fluent-button[data-view=logs]').length === 1"), 'gallery connected resource');
            await capture('resources', 'Resources — 接続中のゲーム', '接続されたサンプルゲームと、Logs・Traces・Metrics・Operationsへの導線を表示します。', 'resources');
            await evaluate("document.querySelector('[data-tab=overview]').click()");
            await waitFor(() => evaluate("document.getElementById('page-title')?.textContent === 'Overview' && Boolean(document.getElementById('recent-events'))"), 'gallery overview');
            await capture('overview', 'Overview — ゲームの概要', '選択したゲームが公開するサブシステムと操作数、最近受信したイベントを確認できます。', 'overview');
        }
        await evaluate("document.querySelector('[data-tab=input]').click()");
        await waitFor(() => evaluate("Boolean(document.querySelector('#input-forms form'))"), 'generated Input form');
        await evaluate(`{ const form = document.querySelector('#input-forms form'); form.elements.namedItem('button').value = 'Jump'; form.elements.namedItem('pressed').value = 'true'; form.elements.namedItem('duration-ms').value = '5000'; for (const input of form.querySelectorAll('input,select')) input.dispatchEvent(new Event('change', { bubbles: true })); form.requestSubmit(); }`);
        await waitFor(() => evaluate("document.getElementById('operation-result').textContent.includes('\"status\": \"success\"')"), `${transport} operation result`);
        await waitFor(() => logs.get(transport).includes('Input state: Jump=True'), `${transport} input change`);
        if (transport === 'http') await capture('input-success', 'Input — オーバーライドの実行', '公開カタログから生成されたフォームでJumpを押下し、ゲーム側の入力が変更された後の実行結果です。', 'input');
        await evaluate("document.querySelector('[data-tab=settings]').click()");
        await waitFor(() => evaluate("Boolean(document.querySelector('[data-setting-path=\"/output/sampleRate\"]'))"), 'complete settings tree');
        assert.equal(await evaluate("Boolean(document.querySelector('#settings-page fluent-accordion')) && Boolean(document.querySelector('#settings-page fluent-tree')) && Boolean(document.querySelector('#settings-page fluent-text-input')) && Boolean(document.querySelector('#settings-page fluent-switch'))"), true, 'Fluent UI settings components');
        assert.equal(await evaluate("{ const rows = [...document.querySelectorAll('.settings-leaf')].map(item => item.getBoundingClientRect()).sort((a, b) => a.top - b.top); rows.every((row, index) => index === 0 || row.top >= rows[index - 1].bottom); }"), true, 'settings tree rows do not overlap');
        assert.equal(await evaluate("document.querySelector('#settings-logs fluent-accordion-item')?.getAttribute('aria-expanded') === 'true'"), false, 'settings logs collapsed by default');
        assert.equal(await evaluate("{ const body = document.getElementById('settings-log-body'); const style = getComputedStyle(body); style.resize === 'vertical' && style.overflowY === 'auto' && style.minHeight === '120px' && style.maxHeight !== 'none'; }"), true, 'settings logs have a bounded resizable height');
        assert.equal(await evaluate("(() => { const body = document.getElementById('settings-log-body'); body.style.height = '360px'; return getComputedStyle(body).height === '360px'; })()"), true, 'settings log height can change');
        assert.equal(await evaluate("document.fonts.check('12px \\\"Noto Sans CJK JP\\\"', '設定システム')"), true, 'Japanese font installed');
        assert.equal(await evaluate("Boolean(document.querySelector('[data-setting-path=\"/buses/0/name\"]')) && Boolean(document.querySelector('[data-setting-path=\"/labels/locale\"]'))"), true, 'nested arrays and dictionaries visible');
        if (transport === 'http') await capture('settings-read', 'Settings — 設定ツリー', '設定モデル全体をオブジェクトと配列のツリーで表示します。設定システムのログは上部で折りたたまれています。', 'settings', 'desktop', true);
        await evaluate(`{ for (const [path, value] of [['/volume', '0.75'], ['/buses/1/name', '環境効果音']]) { const input = document.querySelector('[data-setting-path="' + path + '"]'); input.value = value; input.dispatchEvent(new Event('change', { bubbles: true })); } document.getElementById('settings-save').click(); }`);
        await waitFor(() => evaluate("document.getElementById('settings-status')?.textContent.includes('保存しました')"), 'settings tree save');
        assert.equal(await evaluate("document.querySelector('[data-setting-path=\"/volume\"]')?.value"), '0.75');
        assert.equal(await evaluate("document.querySelector('[data-setting-path=\"/buses/1/name\"]')?.value"), '環境効果音');
        if (transport === 'http') await capture('settings-saved', 'Settings — ツリーの保存', 'ツリーで編集したscalarを保存し、ゲーム側の検証・永続化完了後に全体を再読み込みした状態です。', 'settings', 'desktop', true);
        const persistedSettings = JSON.parse(await readFile(resolve(profile, `${transport}-settings.json`), 'utf8'));
        assert.equal(persistedSettings.sections.audio.values.volume, 0.75);
        assert.equal(persistedSettings.sections.audio.values.buses[1].name, '環境効果音');
        if (transport === 'http') {
            await command('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
            assert.equal(await evaluate('document.documentElement.scrollWidth <= window.innerWidth'), true, 'mobile settings overflow');
            await capture('settings-mobile', 'Settings — 狭い画面', '幅390pxでもログ、ツールバー、設定ツリーを横にはみ出さず表示します。', 'settings', 'mobile', true);
            await command('Emulation.setDeviceMetricsOverride', { width: 1440, height: 960, deviceScaleFactor: 1, mobile: false });
        }
        await evaluate("document.querySelector('[data-tab=animation]').click()");
        await waitFor(() => evaluate("document.getElementById('page-title')?.textContent === 'Animation' && document.getElementById('animation-diagnostics')?.getAttribute('data-supported') === 'true' && document.querySelectorAll('[data-animation-target]').length === 2"), `${transport} Animation capability`);
        await evaluate("document.querySelector('[data-animation-target=\"sample.playback\"]').click()");
        await waitFor(() => evaluate("document.getElementById('animation-selected')?.textContent === 'sample.playback' && !document.getElementById('animation-pause').disabled"), `${transport} animation playback`);
        await evaluate("document.getElementById('animation-pause').click()");
        await waitFor(() => evaluate("document.querySelector('.animation-status')?.textContent === 'Paused'"), `${transport} pause animation playback`);
        await evaluate("{ const position = document.getElementById('animation-position-input'); position.value = '2500000'; position.dispatchEvent(new Event('change', { bubbles: true })); document.getElementById('animation-seek-form').requestSubmit(); }");
        await waitFor(() => evaluate("document.getElementById('animation-position')?.textContent.includes('2,500,000')"), `${transport} seek animation playback`);
        await evaluate("{ const speed = document.getElementById('animation-speed-input'); speed.value = '1.5'; speed.dispatchEvent(new Event('change', { bubbles: true })); document.getElementById('animation-speed-form').requestSubmit(); }");
        await waitFor(() => evaluate("document.getElementById('animation-speed')?.textContent === '1.5×'"), `${transport} animation speed`);
        if (transport === 'http') await capture('animation-playback', 'Animation — Playbackの制御', '明示公開されたPlaybackを選択し、Pause、Seek、速度変更を所有スレッド上の診断操作として実行した状態です。', 'animation', 'desktop', true);
        await evaluate("document.querySelector('[data-animation-target=\"sample.motion\"]').click()");
        await waitFor(() => evaluate("document.getElementById('animation-selected')?.textContent === 'sample.motion' && document.getElementById('animation-state')?.textContent === 'Idle'"), `${transport} animation state machine`);
        await evaluate("{ const trigger = document.getElementById('animation-trigger-input'); trigger.value = 'action'; trigger.dispatchEvent(new Event('change', { bubbles: true })); document.getElementById('animation-trigger-form').requestSubmit(); }");
        await waitFor(() => evaluate("!document.getElementById('animation-read').disabled"), `${transport} animation trigger result`);
        await delay(100);
        await evaluate("document.getElementById('animation-read').click()");
        await waitFor(() => evaluate("document.getElementById('animation-state')?.textContent === 'Action'"), `${transport} animation transition`);
        if (transport === 'http') {
            await capture('animation-state-machine', 'Animation — 状態機械', '公開Triggerを設定し、実行中のアニメーション状態機械がIdleからActionへ遷移した状態です。', 'animation', 'desktop', true);
            await command('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
            assert.equal(await evaluate('document.documentElement.scrollWidth <= window.innerWidth'), true, 'mobile Animation overflow');
            await capture('animation-mobile', 'Animation — 狭い画面', '幅390pxで公開対象、状態、再生・Trigger操作を縦に配置した画面です。', 'animation', 'mobile', true);
            await command('Emulation.setDeviceMetricsOverride', { width: 1440, height: 960, deviceScaleFactor: 1, mobile: false });
        }
        await evaluate("document.querySelector('[data-tab=rendering]').click()");
        await waitFor(() => evaluate("document.getElementById('page-title')?.textContent === 'Rendering' && document.getElementById('render-graph')?.getAttribute('data-supported') === 'true'"), `${transport} Rendering capability`);
        assert.equal(await evaluate("document.getElementById('rg-live').checked"), false, 'Rendering live refresh starts disabled');
        await waitFor(() => evaluate("BigInt(document.getElementById('rg-summary')?.getAttribute('data-compiles') || '0') > 0n && !document.getElementById('rg-refresh-summary').disabled && !document.getElementById('rg-request-capture').disabled"), `${transport} initial render graph summary ready`);
        assert.equal(await evaluate("document.querySelectorAll('#rg-detail-rows tr').length"), 0, 'summary does not request capture details');
        await evaluate("document.getElementById('rg-graph-name').value = 'sample-frame'; document.getElementById('rg-graph-name').dispatchEvent(new Event('change', { bubbles: true })); document.getElementById('rg-capture-form').requestSubmit();");
        await waitFor(() => evaluate("document.getElementById('rg-capture-state')?.getAttribute('data-state') === 'compiled'"), `${transport} real compiled render graph capture`);
        await waitFor(() => evaluate("document.querySelectorAll('#rg-detail-rows tr[data-index]').length === 10 && document.querySelector('#rg-detail-rows tr')?.getAttribute('data-index') === '0'"), `${transport} first capture page`);
        const renderCaptureId = await evaluate("document.getElementById('rg-capture-id').value");
        assert.match(renderCaptureId, /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i);
        const captureDetails = await evaluate("document.getElementById('rg-capture-details').textContent");
        assert.ok(captureDetails.includes('sample-frame'), 'capture identifies the sampled graph');
        assert.ok(captureDetails.includes(renderCaptureId), 'capture identifies its immutable snapshot');
        const captureCounts = await evaluate("Object.fromEntries([...document.querySelectorAll('#rg-capture-details > div')].map(item => [item.querySelector('dt').textContent.trim(), item.querySelector('dd').textContent.trim().replace(/\\s+/g, ' ')]))");
        assert.equal(captureCounts['Pass / culled'], '17 / 2', 'capture reports live and culled pass counts');
        assert.equal(captureCounts['Resource / transient'], '13 / 13', 'capture reports resource counts');
        assert.equal(captureCounts['Dependency / barrier'], '27 / 27', 'capture reports dependency and barrier counts');
        assert.ok((await evaluate("document.getElementById('rg-detail-rows').textContent")).includes('obsolete-scene-upload'), 'capture includes culled passes');
        if (transport === 'http') await capture('rendering-passes', 'Rendering — レンダーグラフのキャプチャ', '実行中のサンプルから要求時に取得したレンダーグラフです。固定されたCapture IDで、除去されたパスを含む最初の10件を表示します。', 'rendering', 'desktop', true);
        await evaluate("document.getElementById('rg-stop-poll').click()");
        await waitFor(() => evaluate("document.getElementById('rg-stop-poll') === null"), `${transport} stop capture polling`);
        const beforeLiveCompiles = await evaluate("document.getElementById('rg-summary').getAttribute('data-compiles')");
        await evaluate("document.getElementById('rg-live').click()");
        await waitFor(() => evaluate(`document.getElementById('rg-live').checked && BigInt(document.getElementById('rg-summary').getAttribute('data-compiles')) > BigInt('${beforeLiveCompiles}')`), `${transport} live aggregate refresh`);
        await evaluate("document.getElementById('rg-live').click()");
        await waitFor(() => evaluate("!document.getElementById('rg-live').checked && document.querySelector('#render-graph > .rg-heading .rg-badge')?.textContent === '集計表示を停止中' && !document.getElementById('rg-refresh-summary').disabled"), `${transport} pause aggregate refresh`);
        const pausedCompiles = await evaluate("document.getElementById('rg-summary').getAttribute('data-compiles')");
        await delay(1200);
        assert.equal(await evaluate("document.getElementById('rg-summary').getAttribute('data-compiles')"), pausedCompiles, 'paused aggregate display stays fixed');
        assert.equal(await evaluate("document.getElementById('rg-capture-id').value"), renderCaptureId, 'live aggregate updates preserve the selected capture');
        assert.equal(await evaluate("document.getElementById('rg-capture-details').textContent"), captureDetails, 'live aggregate updates preserve captured graph and frame identity');
        await evaluate("document.getElementById('rg-next').click()");
        await waitFor(() => evaluate("document.querySelector('#rg-detail-rows tr')?.getAttribute('data-index') === '10' && document.querySelectorAll('#rg-detail-rows tr[data-index]').length === 9"), `${transport} second capture page`);
        assert.ok((await evaluate("document.getElementById('rg-detail-rows').textContent")).includes('unused-debug'), 'second page includes the final culled pass');
        assert.equal(await evaluate("document.getElementById('rg-next').disabled"), true, 'last capture page has no next page');
        await evaluate("document.getElementById('rg-prev').click()");
        await waitFor(() => evaluate("document.querySelector('#rg-detail-rows tr')?.getAttribute('data-index') === '0' && document.querySelectorAll('#rg-detail-rows tr[data-index]').length === 10"), `${transport} previous capture page`);
        for (const detail of ['resources', 'dependencies', 'barriers']) {
            const previousRows = await evaluate("document.getElementById('rg-detail-rows').textContent");
            await evaluate(`document.querySelector('[data-rg-detail=${detail}]').click()`);
            await waitFor(() => evaluate(`document.querySelector('#rg-detail-rows tr')?.getAttribute('data-index') === '0' && document.querySelectorAll('#rg-detail-rows tr[data-index]').length > 0 && document.getElementById('rg-detail-rows').textContent !== ${JSON.stringify(previousRows)}`), `${transport} ${detail} capture details`);
            assert.ok(await evaluate("document.querySelectorAll('#rg-detail-rows tr[data-index]').length <= 10"), 'details fetch only one bounded page');
            if (transport === 'http' && detail === 'resources') await capture('rendering-resources', 'Rendering — リソースの使用範囲', 'キャプチャに保存されたリソースの種別と所有形態、graph内で使用される最初と最後のPassを表示します。使用範囲の図はGPU allocationの寿命を示すものではありません。', 'rendering', 'desktop', true);
            if (transport === 'http' && detail === 'barriers') await capture('rendering-barriers', 'Rendering — リソース同期', '同じCapture IDに保存されたBarrier一覧です。書き込みと読み取りの間に必要な同期を、パスとリソースごとに確認できます。', 'rendering', 'desktop', true);
        }
        const beforeSummaryRefresh = await evaluate("document.getElementById('rg-summary').getAttribute('data-compiles')");
        await delay(50);
        await evaluate("document.getElementById('rg-refresh-summary').click()");
        await waitFor(() => evaluate(`BigInt(document.getElementById('rg-summary').getAttribute('data-compiles')) > BigInt('${beforeSummaryRefresh}') && !document.getElementById('rg-refresh-summary').disabled && !document.getElementById('rg-open-capture').disabled`), `${transport} refreshed render graph summary`);
        assert.equal(await evaluate("document.getElementById('rg-capture-id').value"), renderCaptureId, 'live frames do not replace the capture');
        assert.equal(await evaluate("document.getElementById('rg-capture-details').textContent"), captureDetails, 'captured graph and frame identity stays stable');
        await evaluate(`document.getElementById('rg-capture-id').value = '${randomUUID()}'; document.getElementById('rg-capture-id').dispatchEvent(new Event('change', { bubbles: true })); document.getElementById('rg-open-form').requestSubmit();`);
        await waitFor(() => evaluate("document.getElementById('rg-error')?.textContent.includes('not-found') && !document.getElementById('rg-open-capture').disabled"), `${transport} unknown capture feedback`);
        await evaluate(`document.getElementById('rg-capture-id').value = '${renderCaptureId}'; document.getElementById('rg-capture-id').dispatchEvent(new Event('change', { bubbles: true })); document.getElementById('rg-open-form').requestSubmit();`);
        await waitFor(() => evaluate("document.getElementById('rg-capture-state')?.getAttribute('data-state') === 'compiled' && document.querySelectorAll('#rg-detail-rows tr[data-index]').length > 0 && !(document.getElementById('rg-error')?.textContent.trim())"), `${transport} reopen retained capture`);
        assert.equal(await evaluate("document.getElementById('rg-capture-details').textContent"), captureDetails);
        if (transport === 'http') {
            await command('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
            assert.equal(await evaluate('document.documentElement.scrollWidth <= window.innerWidth'), true, 'mobile Rendering overflow');
            await capture('rendering-mobile', 'Rendering — 狭い画面', '幅390pxで要求時キャプチャの操作と詳細を表示しています。長いIDや同期情報を含む表は画面幅の中で確認できます。', 'rendering', 'mobile', true);
            await command('Emulation.setDeviceMetricsOverride', { width: 1440, height: 960, deviceScaleFactor: 1, mobile: false });
        }
        await evaluate("document.querySelector('[data-tab=metrics]').click()");
        await waitFor(() => evaluate("document.getElementById('page-title')?.textContent === 'Metrics'"), `${transport} leave Rendering`);
        assert.equal(await evaluate("document.getElementById('render-graph') === null"), true, 'leaving Rendering removes capture display');
        await waitFor(() => evaluate("document.getElementById('event-table')?.textContent.includes('9007199254740993')"), 'lossless Int64 Metric');
        await waitFor(() => evaluate("document.getElementById('event-table')?.textContent.includes('settings.operations')"), 'settings Metric');
        await evaluate("document.querySelector('[data-tab=logs]').click()");
        await waitFor(() => evaluate("document.getElementById('event-table')?.textContent.includes('Game instance connected')"), 'Log display');
        await waitFor(() => evaluate("document.getElementById('event-table')?.textContent.includes('Settings save completed with Saved')"), 'settings Log');
        assert.equal(await evaluate("document.querySelector('#event-table [data-virtualized=true]') !== null && document.querySelector('#event-table .log-search fluent-text-input') !== null"), true, 'shared virtualized log viewer');
        await evaluate("{ const search = document.querySelector('#event-table .log-search fluent-text-input'); search.value = 'Settings save completed'; search.dispatchEvent(new Event('input', { bubbles: true })); }");
        await waitFor(() => evaluate("document.querySelectorAll('#event-table .log-row').length === 1 && document.getElementById('event-table').textContent.includes('Settings save completed')"), 'shared log search');
        await evaluate("{ const search = document.querySelector('#event-table .log-search fluent-text-input'); search.value = ''; search.dispatchEvent(new Event('input', { bubbles: true })); document.querySelector('#event-table fluent-switch').click(); }");
        await waitFor(() => evaluate("document.querySelector('#event-table th')?.textContent === '経過時刻'"), 'log elapsed time toggle');
        await waitFor(() => evaluate("document.getElementById('event-table')?.textContent.includes('Game instance connected')"), 'cleared shared log search');
        await evaluate("[...document.querySelectorAll('#event-table tr')].find(row => row.textContent.includes('Game instance connected')).querySelector('fluent-button').click()");
        await waitFor(() => evaluate("document.getElementById('event-detail')?.textContent.includes('\"kind\": \"log\"')"), 'log detail');
        await waitFor(() => evaluate("document.querySelector('#event-table [data-log-structure=true]')?.textContent.includes('log.level')"), 'structured log fields');
        const log = JSON.parse(await evaluate("document.getElementById('event-detail').textContent"));
        if (transport === 'http') await capture('logs-detail', 'Logs — イベント詳細', 'ゲーム接続と設定処理のログを表示します。選択したログの詳細から、同じTraceのイベントへ移動できます。', 'logs');
        await evaluate("document.querySelector('[data-tab=traces]').click()");
        await waitFor(() => evaluate("document.getElementById('event-table')?.textContent.includes('remote.start')"), 'Trace display');
        await waitFor(() => evaluate("document.getElementById('event-table')?.textContent.includes('Settings.save')"), 'settings Trace');
        await evaluate("[...document.querySelectorAll('#event-table tr')].find(row => row.textContent.includes('remote.start')).querySelector('fluent-button').click()");
        await waitFor(() => evaluate("document.getElementById('event-detail')?.textContent.includes('\"kind\": \"span\"')"), 'span detail');
        const trace = JSON.parse(await evaluate("document.getElementById('event-detail').textContent"));
        assert.equal(trace.traceId, log.traceId);
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
        results.push({ transport, publishedServer: true, cookieLogin: true, generatedOperation: true, settingsTreeReadSavePoll: true, settingsFilePersisted: true, settingsTelemetry: true, japaneseFont: true, animationPlaybackControl: true, animationStateMachineTrigger: true, renderGraphSummary: true, renderGraphLivePause: true, realRenderGraphCapture: true, renderGraphCaptureCounts: true, renderGraphPollingStopped: true, renderGraphPagination: true, stableCaptureIdentity: true, retainedCaptureReopened: true, captureNotFoundFeedback: true, inputChanged: true, metricInt64: '9007199254740993', traceLogCorrelated: true, disconnectReleasedInput: true, mobileOverflow: false });
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
    await evaluate("document.querySelector('[data-tab=resources]').click()");
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
    await capture('traces-hierarchy', 'Traces — Spanの親子関係', '検証用イベントを診断サーバーへ送信し、親Spanと子Spanの階層・処理時間・関連Logsへの導線を表示した実画面です。', 'traces');
    await evaluate("document.querySelector('#trace-summary fluent-button[data-correlated=logs]').click()");
    await waitFor(() => evaluate("document.getElementById('page-title')?.textContent === 'Logs' && document.getElementById('event-table')?.textContent.includes('failure')"), 'Trace to Logs');
    assert.ok(!(await evaluate("document.getElementById('event-table').textContent")).includes('wrong-trace'));
    assert.equal(await evaluate('new URL(location.href).pathname'), `/games/${first.sessionId}/logs`);
    await command('Page.navigate', { url: `${origin}/games/${first.sessionId}/ui` });
    await waitFor(() => evaluate("document.getElementById('workspace')?.getAttribute('data-ready') === 'true' && document.getElementById('page-title')?.textContent === 'UI'"), 'unsupported UI page');
    await waitFor(() => evaluate("document.querySelector('.content').textContent.includes('未対応')"), 'capability status');
    assert.equal(await evaluate("document.querySelector('[data-tab=ui]') === null"), true);
    await command('Page.navigate', { url: `${origin}/games/${first.sessionId}/rendering` });
    await waitFor(() => evaluate("document.getElementById('workspace')?.getAttribute('data-ready') === 'true' && document.getElementById('page-title')?.textContent === 'Rendering'"), 'unsupported Rendering page');
    await waitFor(() => evaluate("document.querySelector('.content').textContent.includes('未対応')"), 'Rendering capability status');
    assert.equal(await evaluate("document.querySelector('[data-tab=rendering]') === null"), true, 'Rendering navigation requires a published capability');
    assert.equal(await evaluate("document.getElementById('rg-capture-details') === null"), true, 'another resource cannot expose the previous capture');
    await command('Page.navigate', { url: `${origin}/games/${first.sessionId}/animation` });
    await waitFor(() => evaluate("document.getElementById('workspace')?.getAttribute('data-ready') === 'true' && document.getElementById('page-title')?.textContent === 'Animation'"), 'unsupported Animation page');
    await waitFor(() => evaluate("document.querySelector('.content').textContent.includes('未対応')"), 'Animation capability status');
    assert.equal(await evaluate("document.querySelector('[data-tab=animation]') === null"), true, 'Animation navigation requires a published capability');
    assert.equal(await evaluate("document.getElementById('animation-selected') === null"), true, 'another resource cannot expose the previous animation target');
    await command('Page.navigate', { url: `${origin}/games/${randomUUID()}/logs` });
    await waitFor(() => evaluate("document.getElementById('workspace-error')?.textContent.includes('接続されていません')"), 'disconnected target');
    assert.equal(await evaluate("document.getElementById('session-id').textContent"), '—');
    await command('Page.navigate', { url: `${origin}/games/${first.sessionId}/metrics` });
    await waitFor(() => evaluate("document.getElementById('metric-charts')?.textContent.includes('9223372036854775807')"), 'metric series chart');
    const points = await evaluate("document.querySelector('#metric-charts polyline').getAttribute('points')");
    assert.equal(points, '0,72 150,40 300,8');
    await capture('metrics-chart', 'Metrics — 観測値の推移', '検証用のInt64メトリックを受信順に表示します。大きな整数でも値を保持したままグラフと一覧で確認できます。', 'metrics');
    await evaluate(`document.getElementById('game-select').value = '${second.sessionId}'; document.getElementById('game-select').dispatchEvent(new Event('change', { bubbles: true })); document.querySelector('[data-tab=logs]').click();`);
    await waitFor(() => evaluate("document.getElementById('event-table')?.textContent.includes('other-resource')"), 'switch resource');
    assert.ok(!(await evaluate("document.getElementById('event-table').textContent")).includes('later-error'));
    await gameRequest('DELETE', `sessions/${first.sessionId}`, undefined, first.sessionSecret);
    await gameRequest('DELETE', `sessions/${second.sessionId}`, undefined, second.sessionSecret);
    results.push({ scenario: 'aspire-dashboard-patterns', resourceNavigation: true, gamePageRoutes: true, unsupportedCapability: true, animationCapabilityIsolation: true, renderGraphCapabilityIsolation: true, disconnectedTargetIsolation: true, severityFilter: true, pauseResume: true, traceDeepLink: true, spanHierarchy: true, traceToLogs: true, metricSeriesChart: true, int64ChartPrecision: true, resourceIsolation: true, untrustedText: true });
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
    assert.equal(await evaluate("(document.getElementById('operation-result')?.textContent ?? '')", attached.sessionId), '');
    await command('Target.closeTarget', { targetId: otherPage.targetId });
    results.push({ scenario: 'blazor-server', signalRWebSocket: true, fluentUiComponents: true, noLegacySse: true, logoutRevokesOtherCircuit: true });
    assert.deepEqual(errors, [], 'browser errors');
    await writeFile(resolve(output, 'results.json'), JSON.stringify(results, null, 2) + '\n');
    await writeFile(resolve(output, 'gallery.json'), JSON.stringify({ version: 1, capturedAt: new Date().toISOString(), screenshots }, null, 2) + '\n');
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

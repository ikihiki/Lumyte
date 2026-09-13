import { spawn } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomUUID } from 'node:crypto';
import { createServer } from 'node:http';

const sourceDirectory = path.dirname(fileURLToPath(import.meta.url));
const repository = path.resolve(sourceDirectory, '..', '..', '..');
const outputDirectory = path.join(repository, 'artifacts', 'experiments', 'browser-webgpu-indirect-immediates', randomUUID());
await mkdir(outputDirectory, { recursive: true });
const server = createServer((request, response) => {
    response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
    response.end('<!doctype html><title>WebGPU indirect immediate probe</title>');
});
await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
const pageUrl = `http://127.0.0.1:${server.address().port}/`;
const executable = process.argv[2] ?? process.env.LUMYTE_WEBGPU_BROWSER ?? 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const child = spawn(executable, ['--headless=new', '--no-first-run', '--no-default-browser-check', '--remote-debugging-port=0',
    `--user-data-dir=${path.join(outputDirectory, 'profile')}`, 'about:blank'], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
let log = '';
let socket;
let nextId = 0;
const requests = new Map();
const events = new Map();
function expectEvent(method, sessionId) {
    return new Promise((resolve, reject) => {
        const key = `${sessionId}:${method}`;
        const timer = setTimeout(() => { events.delete(key); reject(new Error(`CDP event timed out: ${method}`)); }, 45000);
        events.set(key, { resolve, timer });
    });
}
function command(method, params = {}, sessionId) {
    const id = ++nextId;
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => { requests.delete(id); reject(new Error(`CDP timed out: ${method}`)); }, 45000);
        requests.set(id, { resolve, reject, timer });
        socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
    });
}
const endpoint = new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('Browser startup timed out.')), 30000);
    child.once('error', error => { clearTimeout(timer); reject(error); });
    child.once('exit', code => { clearTimeout(timer); reject(new Error(`Browser exited with ${code}.`)); });
    child.stderr.on('data', data => {
        log += data;
        const match = log.match(/DevTools listening on (ws:\/\/[^\s]+)/);
        if (match) { clearTimeout(timer); resolve(match[1]); }
    });
    child.stdout.on('data', data => { log += data; });
});
try {
    socket = new WebSocket(await endpoint);
    await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
    socket.onmessage = event => {
        const response = JSON.parse(event.data);
        if (response.method) {
            const key = `${response.sessionId}:${response.method}`;
            const signal = events.get(key);
            if (signal) { events.delete(key); clearTimeout(signal.timer); signal.resolve(response.params); }
        }
        const request = requests.get(response.id);
        if (!request) return;
        requests.delete(response.id);
        clearTimeout(request.timer);
        if (response.error) request.reject(new Error(JSON.stringify(response.error)));
        else request.resolve(response.result);
    };
    const version = await command('Browser.getVersion');
    const { targetId } = await command('Target.createTarget', { url: 'about:blank' });
    const { sessionId } = await command('Target.attachToTarget', { targetId, flatten: true });
    await command('Runtime.enable', {}, sessionId);
    await command('Page.enable', {}, sessionId);
    const loaded = expectEvent('Page.loadEventFired', sessionId);
    await command('Page.navigate', { url: pageUrl }, sessionId);
    await loaded;
    const code = await readFile(path.join(sourceDirectory, 'probe.js'), 'utf8');
    const response = await command('Runtime.evaluate', { expression: `${code}\nrunIndirectImmediateProbe()`, awaitPromise: true, returnByValue: true }, sessionId);
    const result = { timestamp: new Date().toISOString(), browser: version, result: response.result?.value, exceptionDetails: response.exceptionDetails };
    await writeFile(path.join(outputDirectory, 'result.json'), JSON.stringify(result, null, 2));
    console.log(JSON.stringify(result, null, 2));
    console.log(`Saved: ${path.join(outputDirectory, 'result.json')}`);
    if (!result.result?.passed || result.exceptionDetails) process.exitCode = 1;
} finally {
    if (socket?.readyState === WebSocket.OPEN) {
        try { await command('Browser.close'); } catch {}
        socket.close();
    }
    await writeFile(path.join(outputDirectory, 'browser.log'), log);
    if (child.pid && child.exitCode === null) child.kill();
    await new Promise(resolve => server.close(resolve));
}

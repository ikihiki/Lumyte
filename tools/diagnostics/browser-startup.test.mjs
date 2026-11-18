import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { spawn } from 'node:child_process';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import test from 'node:test';
import { connectDiagnosticBrowser, diagnosticBrowserArguments } from './browser-startup.mjs';

const active = '12345\n/devtools/browser/test\n';
const page = { type: 'page', webSocketDebuggerUrl: 'ws://127.0.0.1:12345/devtools/page/test' };
function child() { return Object.assign(new EventEmitter(), { exitCode: null, signalCode: null, stderr: new EventEmitter() }); }
class Socket extends EventTarget {
    constructor() { super(); queueMicrotask(() => this.dispatchEvent(new Event('open'))); }
    close() { this.closed = true; }
}
const ready = { read: async () => active, request: async () => ({ ok: true, json: async () => [page] }), Socket };

test('readiness does not depend on fragmented or absent/late stderr', async () => {
    for (const fragments of [[], ['DevTools lis', 'tening on ws://127.', '0.0.1:12345/devtools/browser/test']]) {
        const process = child();
        for (const fragment of fragments) process.stderr.emit('data', Buffer.from(fragment));
        const events = [];
        const socket = await connectDiagnosticBrowser(process, '/owned/profile', { ...ready, record: event => events.push(event) });
        assert.ok(socket instanceof Socket);
        assert.match(events.at(-1), /^\+\d+ms page CDP transport open$/);
        process.stderr.emit('data', 'DevTools listening on ws://127.0.0.1:12345/devtools/browser/test');
        assert.equal(process.listenerCount('exit'), 0);
        socket.close();
    }
});

test('partial rendezvous file and not-yet-created page are not readiness', async () => {
    let reads = 0, requests = 0;
    const socket = await connectDiagnosticBrowser(child(), '/profile', { ...ready,
        read: async () => ++reads === 1 ? '12345\n' : active, pause: async () => {},
        request: async () => ({ ok: true, json: async () => ++requests === 1 ? [] : [page] }),
    });
    assert.equal(reads, 3); assert.equal(requests, 2); socket.close();
});

test('spawn errors and early exit fail immediately instead of timing out', async () => {
    for (const kind of ['error', 'exit']) {
        const process = child();
        const result = connectDiagnosticBrowser(process, '/profile', { ...ready, read: () => new Promise(() => {}) });
        if (kind === 'error') process.emit('error', new Error('spawn ENOENT'));
        else process.emit('exit', 23, null);
        await assert.rejects(result, kind === 'error' ? /spawn ENOENT/ : /exited \(code=23/);
        assert.equal(process.listenerCount('exit'), 0);
    }
});

test('deadline bounds missing file, hung discovery and hung WebSocket; no late success', async () => {
    let stalledSocket;
    class StalledSocket extends EventTarget { constructor() { super(); stalledSocket = this; } close() { this.closed = true; } }
    for (const overrides of [
        { read: async () => { throw Object.assign(new Error('missing'), { code: 'ENOENT' }); } },
        { read: () => new Promise(() => {}) },
        { request: () => new Promise(() => {}) },
        { request: async () => ({ ok: true, json: () => new Promise(() => {}) }) },
        { Socket: StalledSocket },
    ]) {
        await assert.rejects(connectDiagnosticBrowser(child(), '/profile', { ...ready, timeout: 25, ...overrides }), /limit 25ms.*startup deadline reached/);
    }
    assert.equal(stalledSocket.closed, true);
    stalledSocket.dispatchEvent(new Event('open'));
});

test('late discovery completion cannot create a socket after deadline', async () => {
    let finish, created = 0;
    class CountedSocket extends Socket { constructor() { super(); created++; } }
    const result = connectDiagnosticBrowser(child(), '/profile', { ...ready, timeout: 20, Socket: CountedSocket,
        request: () => new Promise(resolve => { finish = resolve; }),
    });
    await assert.rejects(result, /startup deadline reached/);
    finish({ ok: true, json: async () => [page] });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(created, 0);
});

test('real child spawn failure is reported without waiting for the deadline', async () => {
    const browser = spawn(resolve(tmpdir(), 'lumyte-nonexistent-chrome', 'missing'), [], { stdio: 'ignore' });
    await assert.rejects(connectDiagnosticBrowser(browser, '/profile'), /ENOENT/);
});

test('CDP transport errors fail rather than accepting the file alone', async () => {
    class BrokenSocket extends EventTarget {
        constructor() { super(); queueMicrotask(() => this.dispatchEvent(new Event('error'))); }
        close() {}
    }
    await assert.rejects(connectDiagnosticBrowser(child(), '/profile', { ...ready, Socket: BrokenSocket }), /CDP WebSocket failed/);
});

test('real headless Chrome opens page transport within the original 15s budget', { skip: !process.env.CHROME }, async () => {
    const profile = await mkdtemp(resolve(tmpdir(), 'lumyte-startup-test-'));
    const browser = spawn(process.env.CHROME, diagnosticBrowserArguments(profile), { stdio: ['ignore', 'ignore', 'pipe'] });
    let stderr = ''; browser.stderr.on('data', data => { stderr = (stderr + data).slice(-8192); });
    try {
        const socket = await connectDiagnosticBrowser(browser, profile);
        socket.close();
    } catch (error) { throw new Error(error.message + '\n' + stderr, { cause: error }); }
    finally {
        if (browser.exitCode === null && browser.signalCode === null) {
            const stopped = new Promise(resolve => browser.once('exit', resolve));
            const timer = setTimeout(() => browser.kill('SIGKILL'), 2000);
            browser.kill('SIGTERM'); await stopped; clearTimeout(timer);
        }
        await rm(profile, { recursive: true, force: true, maxRetries: 3 });
    }
});

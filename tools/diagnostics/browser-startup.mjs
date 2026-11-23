import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

export function diagnosticBrowserArguments(profile) {
    return ['--headless', '--no-sandbox', '--disable-dev-shm-usage', '--no-first-run', '--no-default-browser-check',
        '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'];
}

// Chrome's own profile rendezvous file is independent of stderr delivery. A file
// alone is not readiness: require a page target AND an open CDP transport.
export async function connectDiagnosticBrowser(child, profile, {
    timeout = 15000, read = readFile, request = fetch, Socket = WebSocket,
    now = () => performance.now(), pause = delay, record = () => {},
} = {}) {
    const started = now();
    const deadline = started + timeout;
    let failure;
    let last = 'waiting for DevToolsActivePort';
    let socket;
    const abort = new AbortController();
    let rejectDeadline;
    const expired = new Promise((_, reject) => { rejectDeadline = reject; });
    function fail(error) { failure ??= error; abort.abort(); rejectDeadline(failure); }
    const timer = setTimeout(() => fail(new Error('startup deadline reached')), timeout);
    const onError = error => fail(error);
    const onExit = (code, signal) => fail(new Error(`Chromium exited (code=${code}, signal=${signal})`));
    child.once('error', onError);
    child.once('exit', onExit);
    function note(message) { last = message.slice(0, 512); record(`+${Math.round(now() - started)}ms ${last}`); }
    function checkDeadline() {
        if (failure || remaining() <= 0) throw failure ?? new Error('startup deadline reached');
    }
    const remaining = () => Math.max(0, deadline - now());
    async function connect() {
        if (child.exitCode !== null || child.signalCode !== null) onExit(child.exitCode, child.signalCode);
        while (remaining() > 0) {
            if (failure) throw failure;
            let contents;
            try { contents = await read(resolve(profile, 'DevToolsActivePort'), 'utf8'); }
            catch (error) { if (error.code !== 'ENOENT') throw error; }
            checkDeadline();
            const [port, browserPath] = (contents ?? '').trim().split(/\r?\n/);
            if (/^\d+$/.test(port) && Number(port) > 0 && Number(port) <= 65535 && browserPath?.startsWith('/devtools/browser/')) {
                note('DevToolsActivePort available');
                let pages;
                try {
                    const response = await request(`http://127.0.0.1:${port}/json/list`, { signal: abort.signal });
                    if (response.ok) pages = await response.json();
                } catch (error) { if (failure) throw failure; note(`CDP discovery pending: ${error.message}`); }
                checkDeadline();
                const page = pages?.find(page => page.type === 'page');
                if (page) {
                    const url = new URL(page.webSocketDebuggerUrl);
                    if (url.protocol !== 'ws:' || url.hostname !== '127.0.0.1' || url.port !== port || !url.pathname.startsWith('/devtools/page/')) throw new Error('Invalid local CDP page endpoint');
                    socket = new Socket(url.href);
                    await new Promise((resolve, reject) => {
                        const open = () => { cleanup(); resolve(); };
                        const error = () => { cleanup(); reject(new Error('CDP WebSocket failed to open')); };
                        const stopped = () => { cleanup(); reject(failure ?? new Error('CDP startup aborted')); };
                        function cleanup() { socket.removeEventListener('open', open); socket.removeEventListener('error', error); abort.signal.removeEventListener('abort', stopped); }
                        socket.addEventListener('open', open, { once: true });
                        socket.addEventListener('error', error, { once: true });
                        abort.signal.addEventListener('abort', stopped, { once: true });
                        if (abort.signal.aborted) stopped();
                    });
                    if (failure || remaining() <= 0) throw failure ?? new Error('startup deadline reached');
                    note('page CDP transport open');
                    return socket;
                }
                note('waiting for page CDP target');
            }
            await pause(Math.min(100, remaining()), undefined, { signal: abort.signal });
        }
        throw new Error('startup deadline reached');
    }
    note('Chromium spawned; waiting for DevToolsActivePort');
    try { return await Promise.race([connect(), expired]); }
    catch (error) {
        socket?.close();
        throw new Error(`Chromium startup failed after ${Math.round(now() - started)}ms (limit ${timeout}ms; ${last}): ${error.message}`, { cause: error });
    } finally {
        clearTimeout(timer);
        abort.abort();
        child.removeListener('error', onError);
        child.removeListener('exit', onExit);
    }
}

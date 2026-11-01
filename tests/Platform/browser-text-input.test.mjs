import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdtemp, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { extname, resolve, sep } from "node:path";
import { setTimeout as delay } from "node:timers/promises";

const root = resolve(".");
const executable = process.env.CHROME ?? "chromium";
const browserProcessGroup = process.platform !== "win32";
const profile = await mkdtemp(resolve(tmpdir(), "lumyte-ui-"));
const types = { ".html": "text/html", ".js": "text/javascript", ".json": "application/json", ".wasm": "application/wasm" };
const server = createServer(async (request, response) => {
    try {
        const pathname = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
        const path = resolve(root, `.${pathname === "/" ? "/index.html" : pathname}`);
        if (!path.startsWith(`${root}${sep}`)) {
            response.writeHead(403).end();
            return;
        }
        const contents = await readFile(path);
        response.writeHead(200, { "Content-Type": types[extname(path)] ?? "application/octet-stream" }).end(contents);
    } catch {
        response.writeHead(404).end();
    }
});
await new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", resolve);
});

let browser;
let socket;
try {
    await readFile(resolve(root, "tests/Platform/browser-text-input.html"));
    browser = spawn(executable, [
        "--headless", "--no-sandbox", "--disable-dev-shm-usage",
        "--enable-unsafe-webgpu", "--enable-unsafe-swiftshader",
        "--use-vulkan=swiftshader", "--use-angle=swiftshader",
        "--remote-debugging-port=0", `--user-data-dir=${profile}`, "about:blank",
    ], { stdio: ["ignore", "ignore", "pipe"], detached: browserProcessGroup });
    let diagnostics = "";
    let launchError;
    browser.on("error", error => { launchError = error; });
    browser.stderr.on("data", data => { diagnostics += data; });
    const launchDeadline = Date.now() + 60000;
    let debuggerPort;
    while (!debuggerPort && Date.now() < launchDeadline) {
        if (launchError) { throw launchError; }
        const match = /DevTools listening on ws:\/\/127\.0\.0\.1:(\d+)/.exec(diagnostics);
        debuggerPort = match?.[1];
        if (!debuggerPort) { await delay(100); }
    }
    assert.ok(debuggerPort, `Browser did not start: ${diagnostics}`);
    const pages = await (await fetch(`http://127.0.0.1:${debuggerPort}/json/list`)).json();
    const page = pages.find(value => value.type === "page");
    assert.ok(page, "No browser page");
    socket = new WebSocket(page.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => {
        socket.addEventListener("open", resolve, { once: true });
        socket.addEventListener("error", reject, { once: true });
    });
    let nextId = 0;
    const pending = new Map();
    const errors = [];
    const progress = [];
    socket.addEventListener("message", event => {
        const message = JSON.parse(event.data);
        if (message.id) {
            const action = pending.get(message.id);
            pending.delete(message.id);
            if (!action) { return; }
            if (message.error) { action.reject(new Error(JSON.stringify(message.error))); }
            else { action.resolve(message.result); }
        } else if (message.method === "Runtime.exceptionThrown") {
            errors.push(message.params.exceptionDetails.exception?.description ?? message.params.exceptionDetails.text);
        } else if (message.method === "Runtime.consoleAPICalled") {
            const text = message.params.args.map(value => value.description ?? String(value.value)).join(" ");
            if (message.params.type === "error") { errors.push(text); }
            else { progress.push(text); }
        }
    });
    function command(method, params = {}) {
        const id = ++nextId;
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                pending.delete(id);
                reject(new Error(`CDP timeout: ${method}`));
            }, 10000);
            pending.set(id, {
                resolve: value => { clearTimeout(timeout); resolve(value); },
                reject: error => { clearTimeout(timeout); reject(error); },
            });
            socket.send(JSON.stringify({ id, method, params }));
        });
    }
    await command("Runtime.enable");
    await command("Emulation.setDeviceMetricsOverride", { width: 1000, height: 700, deviceScaleFactor: 1, mobile: false });
    await command("Page.bringToFront");
    await command("Page.navigate", { url: `http://127.0.0.1:${server.address().port}/tests/Platform/browser-text-input.html` });
    async function evaluate(expression) {
        const response = await command("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true });
        if (response.exceptionDetails) throw new Error(response.exceptionDetails.exception?.description ?? response.exceptionDetails.text);
        return response.result.value;
    }
    let result="running";
    const deadline=Date.now()+10000;
    while(result==="running" && Date.now()<deadline) {
        result=await evaluate("document.querySelector('#result')?.textContent ?? 'running'");
        if(result==="running")await delay(30);
    }
    assert.equal(result,"PASS 18",`${result}\n${errors.join("\n")}\n${diagnostics}`);
    console.log("Browser textarea lifecycle PASS 18 (synthetic DOM events; no OS IME)");

} finally {
    socket?.close();
    if (browser?.pid && browser.exitCode === null && browser.signalCode === null) {
        function signalBrowser(signal) {
            try {
                if (browserProcessGroup) { process.kill(-browser.pid, signal); }
                else { browser.kill(signal); }
            } catch (error) {
                if (error.code !== "ESRCH") { throw error; }
            }
        }
        const stopped = new Promise(resolve => browser.once("exit", resolve));
        const timeout = setTimeout(() => signalBrowser("SIGKILL"), 2000);
        signalBrowser("SIGTERM");
        await stopped;
        clearTimeout(timeout);
        if (browserProcessGroup) { signalBrowser("SIGKILL"); }
    }
    await new Promise(resolve => server.close(resolve));
    await rm(profile, { recursive: true, force: true, maxRetries: 3 });
}

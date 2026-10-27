import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdtemp, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { extname, resolve, sep } from "node:path";
import { setTimeout as delay } from "node:timers/promises";

const root = resolve(process.argv[2] ?? "samples/Lumyte.Graphics.Text.Parley.Browser.Sample/bin/Release/net10.0/publish/wwwroot");
const executable = process.env.CHROME ?? "chromium";
const browserProcessGroup = process.platform !== "win32";
const profile = await mkdtemp(resolve(tmpdir(), "lumyte-parley-"));
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
    await readFile(resolve(root, "index.html"));
    const frameworkFiles = await readdir(resolve(root, "_framework"));
    assert.ok(!frameworkFiles.some(name => /SixLabors|SkiaSharp|HarfBuzzSharp/.test(name)), "The Parley sample must publish without SixLabors or Skia assemblies.");
    await readFile(resolve(root, "RUST-STANDARD-LIBRARY-NOTICES.html"));
    await readFile(resolve(root, "THIRD-PARTY-NOTICES.md"));
    await readFile(resolve(root, "DISTANCE-FIELD-THIRD-PARTY-NOTICES.md"));
    browser = spawn(executable, [
        "--headless", "--no-sandbox", "--disable-dev-shm-usage",
        "--enable-unsafe-webgpu", "--enable-unsafe-swiftshader",
        "--enable-features=Vulkan", "--use-vulkan=swiftshader",
        "--use-angle=vulkan", "--disable-vulkan-surface",
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
    if (process.env.LUMYTE_SCREENSHOT) {
        await command("Emulation.setDeviceMetricsOverride", { width: 1100, height: 1800, deviceScaleFactor: 1, mobile: false });
    }
    await command("Page.navigate", { url: `http://127.0.0.1:${server.address().port}` });
    const deadline = Date.now() + 180000;
    let result;
    while (Date.now() < deadline) {
        const response = await command("Runtime.evaluate", {
            expression: "JSON.stringify({result:document.querySelector('#report')?.dataset.result,report:document.querySelector('#report')?.textContent})",
            returnByValue: true,
        });
        result = JSON.parse(response.result.value ?? "{}");
        if (result.result) { break; }
        await delay(100);
    }
    assert.equal(result?.result, "passed", `${result?.report ?? "Browser timed out"}\n${progress.slice(-40).join("\n")}\n${errors.join("\n")}\n${diagnostics}`);
    assert.match(result.report, /Parley WASM text rendering passed: [1-9]\d* ink pixels/);
    assert.match(result.report, /MSDF\/MTSDF x4 counters, COLRv1 vector gradients and multiply/);
    assert.match(result.report, /csbindgen C ABI, ligatures, Japanese fallback, bidi clusters and wrapping/);
    assert.match(result.report, /translucent and gradient currentColor/);
    assert.match(result.report, /Layouts survive engine disposal/);
    assert.match(result.report, /Backend checks: all 28 composite modes, isolated backdrops, clip masks and gradient edge cases/);
    assert.deepEqual(errors, [], "Browser reported runtime or console errors.");
    if (process.env.LUMYTE_SCREENSHOT) {
        const metrics = await command("Page.getLayoutMetrics");
        const size = metrics.cssContentSize ?? metrics.contentSize;
        const screenshot = await command("Page.captureScreenshot", {
            format: "png",
            captureBeyondViewport: true,
            clip: { x: 0, y: 0, width: size.width, height: size.height, scale: 1 },
        });
        await writeFile(process.env.LUMYTE_SCREENSHOT, Buffer.from(screenshot.data, "base64"));
    }
    console.log(`Browser .NET/Parley/WebGPU text verification passed\n${result.report}`);
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

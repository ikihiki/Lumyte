import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { extname, resolve, sep } from "node:path";
import { inflateSync } from "node:zlib";
import { setTimeout as delay } from "node:timers/promises";

const root = resolve(process.argv[2] ?? "samples/Lumyte.UI.Application.Browser.Sample/bin/Release/net10.0/publish/wwwroot");
const executable = process.env.CHROME ?? "chromium";
const profile = await mkdtemp(resolve(tmpdir(), "lumyte-application-"));
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
    browser = spawn(executable, [
        "--headless", "--no-sandbox", "--disable-dev-shm-usage",
        "--enable-unsafe-webgpu", "--enable-unsafe-swiftshader",
        "--enable-features=Vulkan", "--use-vulkan=swiftshader",
        "--use-angle=swiftshader", "--disable-vulkan-surface",
        "--remote-debugging-port=0", `--user-data-dir=${profile}`, "about:blank",
    ], { stdio: ["ignore", "ignore", "pipe"] });
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
        } else if (message.method === "Log.entryAdded") {
            const entry = message.params.entry;
            if (entry.source !== "network") {
                if (entry.level === "error" || /Destroyed texture|Validation Error|Error while parsing WGSL|[Ii]nvalid (?:command|bind|pipeline)/.test(entry.text)) errors.push(entry.text);
                else progress.push(entry.text);
            }
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
            }, method === "Runtime.evaluate" ? 45000 : 10000);
            pending.set(id, {
                resolve: value => { clearTimeout(timeout); resolve(value); },
                reject: error => { clearTimeout(timeout); reject(error); },
            });
            socket.send(JSON.stringify({ id, method, params }));
        });
    }
    await command("Runtime.enable");
    await command("Log.enable");
    const throttle = Number(process.env.LUMYTE_BROWSER_CPU_THROTTLE ?? 1);
    assert.ok(Number.isFinite(throttle) && throttle >= 1 && throttle <= 20, "CPU throttle must be between 1 and 20");
    if (throttle > 1) await command("Emulation.setCPUThrottlingRate", { rate: throttle });
    await command("Page.navigate", { url: `http://127.0.0.1:${server.address().port}` });
    async function evaluate(expression) {
        const response = await command("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true });
        if (response.exceptionDetails) throw new Error(progress.join("\n") + "\n" + JSON.stringify(response.exceptionDetails));
        return response.result.value;
    }
    const deadline = Date.now() + 60000;
    while (Date.now() < deadline) {
        assert.deepEqual(errors, [], "Browser startup failed");
        assert.equal(await evaluate("globalThis.lumyteApplicationError"), undefined);
        if (await evaluate("globalThis.lumyteApplicationProbe?.IsPresented() === true")) break;
        await delay(100);
    }
    // Allow the first GPU presentation to complete before capturing or pressing the UI.
    await delay(1500);
    assert.deepEqual(errors, [], "Browser rendering failed: " + await evaluate("globalThis.lumyteApplicationProbe.GetState()"));
    console.log("Before capture: " + await evaluate("globalThis.lumyteApplicationProbe.GetState()"));
    const captured = Buffer.from(await evaluate("globalThis.lumyteApplicationProbe.CapturePngAsync()"), "base64");
    assert.equal(captured.subarray(1, 4).toString(), "PNG");
    assert.equal(captured.readUInt32BE(16), await evaluate("document.querySelector('canvas').width"));
    assert.equal(captured.readUInt32BE(20), await evaluate("document.querySelector('canvas').height"));
    const chunks = [];
    for (let offset = 8; offset < captured.length;) {
        const length = captured.readUInt32BE(offset);
        if (captured.subarray(offset + 4, offset + 8).toString() === "IDAT") chunks.push(captured.subarray(offset + 8, offset + 8 + length));
        offset += length + 12;
    }
    const pixels = inflateSync(Buffer.concat(chunks));
    const width = captured.readUInt32BE(16);
    function pixel(x, y) {
        const row = y * (width * 4 + 1);
        assert.equal(pixels[row], 0, "The application PNG encoder uses unfiltered scanlines");
        return [...pixels.subarray(row + 1 + x * 4, row + 5 + x * 4)];
    }
    const gray = pixel(20, 20);
    assert.ok(gray.slice(0, 3).every(value => Math.abs(value - 128) <= 2), `sRGB 0.5 must remain approximately 128: ${gray}`);
    assert.equal(gray[3], 255);
    assert.deepEqual(pixel(300, 300), [255, 255, 255, 255]);
    const repeats = Number(process.env.LUMYTE_BROWSER_CAPTURE_REPEATS ?? 1);
    assert.ok(Number.isInteger(repeats) && repeats >= 1 && repeats <= 8, "Capture repeats must be between 1 and 8");
    for (let i = 1; i < repeats; i++) {
        const repeated = Buffer.from(await evaluate("globalThis.lumyteApplicationProbe.CapturePngAsync()"), "base64");
        assert.deepEqual(repeated, captured, "Repeated captures of the static page must retain the same GPU pixels");
    }
    await writeFile(process.env.LUMYTE_UI_APPLICATION_HOST_SCREENSHOT ?? resolve(tmpdir(), "lumyte-ui-application-host.png"), captured);
    const image = await command("Page.captureScreenshot", { format: "png" });
    await writeFile(process.env.LUMYTE_UI_APPLICATION_SCREENSHOT ?? resolve(tmpdir(), "lumyte-ui-application-browser.png"), Buffer.from(image.data, "base64"));
    const rect = JSON.parse(await evaluate("JSON.stringify(document.querySelector('canvas').getBoundingClientRect().toJSON())"));
    await command("Input.dispatchMouseEvent", { type: "mousePressed", x: rect.x + 20, y: rect.y + 20, button: "left", buttons: 1, clickCount: 1 });
    await command("Input.dispatchMouseEvent", { type: "mouseReleased", x: rect.x + 20, y: rect.y + 20, button: "left", buttons: 0, clickCount: 1 });
    while (Date.now() < deadline && !await evaluate("globalThis.lumyteApplicationFinished")) {
        assert.deepEqual(errors, [], "Browser input or cleanup failed");
        await delay(50);
    }
    assert.equal(await evaluate("globalThis.lumyteApplicationFinished"), true, "UI click must stop the application");
    assert.equal(await evaluate("document.querySelectorAll('canvas').length"), 1, "Borrowed canvas remains in the DOM");
    assert.deepEqual(errors, [], "Browser reported errors");
    console.log("UI Application Browser WebGPU smoke passed: presentation, automation GPU PNG, normal input, orderly stop and borrowed canvas.");
} finally {
    socket?.close();
    if (browser?.pid && browser.exitCode === null && browser.signalCode === null) {
        const stopped = new Promise(resolve => browser.once("exit", resolve));
        const timeout = setTimeout(() => browser.kill("SIGKILL"), 2000);
        browser.kill("SIGTERM");
        await stopped;
        clearTimeout(timeout);
    }
    await new Promise(resolve => server.close(resolve));
    await rm(profile, { recursive: true, force: true, maxRetries: 3 });
}

import { browserGpuArguments, requiresSoftwareWebGpu, installBrowserGpuAudit, verifySoftwareWebGpu } from "../../tools/graphics/browser-gpu.mjs";
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { extname, resolve, sep } from "node:path";
import { inflateSync } from "node:zlib";
import { setTimeout as delay } from "node:timers/promises";

const root = resolve(process.argv[2] ?? "samples/Lumyte.Maps.Browser.Sample/bin/Release/net10.0/publish/wwwroot");
const executable = process.env.CHROME ?? "chromium";
const profile = await mkdtemp(resolve(tmpdir(), "lumyte-maps-"));
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
        ...browserGpuArguments(process.platform, "swiftshader"),
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
            }, method === "Page.navigate" ? 60000 : method === "Runtime.evaluate" ? 45000 : 10000);
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
    if (requiresSoftwareWebGpu()) {
        await command("Page.enable");
        await command("Page.addScriptToEvaluateOnNewDocument", { source: `(${installBrowserGpuAudit.toString()})();` });
    }
    await command("Page.navigate", { url: `http://127.0.0.1:${server.address().port}` });
    async function evaluate(expression) {
        const response = await command("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true });
        if (response.exceptionDetails) throw new Error(progress.join("\n") + "\n" + JSON.stringify(response.exceptionDetails));
        return response.result.value;
    }
    const deadline = Date.now() + 60000;
    while (Date.now() < deadline) {
        assert.deepEqual(errors, [], "Browser startup failed: " + progress.join("\n"));
        assert.equal(await evaluate("globalThis.lumyteMapsError"), undefined);
        if (await evaluate("globalThis.lumyteMapsProbe?.HasLoadedTile() === true")) break;
        await delay(100);
    }
    assert.equal(await evaluate("globalThis.lumyteMapsProbe?.HasLoadedTile()"), true,
        "Resources must decode and MapSession must actually adopt the MVT; " + await evaluate("globalThis.lumyteMapsProbe?.GetState()"));
    const recorded = await evaluate("globalThis.lumyteMapsProbe.RecordUiAsync()");
    assert.ok(recorded >= 5, "Mounted UI must record actual decoded paths");
    console.log("Actual Resources MVT adoption and mounted UI recording passed: commands=" + recorded + "; retained recording reused unchanged map.");
    if (process.env.LUMYTE_MAPS_GPU_CAPTURE === "1") {
        await delay(1500);
        const captured = Buffer.from(await evaluate("globalThis.lumyteMapsProbe.CapturePngAsync()"), "base64");
        assert.equal(captured.subarray(1, 4).toString(), "PNG");
        const width = captured.readUInt32BE(16);
        const height = captured.readUInt32BE(20);
        assert.equal(width, 800);
        assert.equal(height, 600);
        const chunks = [];
        for (let offset = 8; offset < captured.length;) {
            const count = captured.readUInt32BE(offset);
            if (captured.subarray(offset + 4, offset + 8).toString() === "IDAT") chunks.push(captured.subarray(offset + 8, offset + 8 + count));
            offset += count + 12;
        }
        const pixels = inflateSync(Buffer.concat(chunks));
        const colors = new Set();
        for (let y = 0; y < height; y++) {
            const row = y * (width * 4 + 1);
            assert.equal(pixels[row], 0, "Host PNG uses unfiltered scanlines");
            for (let x = 0; x < width; x++) {
                const at = row + 1 + x * 4;
                colors.add(pixels.subarray(at, at + 4).toString("hex"));
            }
        }
        assert.ok(colors.size > 5, "Adopted map paths, not only a blank background, must reach GPU pixels");
        await writeFile(process.env.LUMYTE_MAPS_SCREENSHOT ?? resolve(tmpdir(), "lumyte-maps-browser.png"), captured);
        console.log("Adopted tile and captured map GPU PNG: " + width + "x" + height + "; colors=" + colors.size);
    }
    while (Date.now() < deadline && !await evaluate("globalThis.lumyteMapsProbe.HasPresentedInputAsync()")) await delay(25);
    assert.equal(await evaluate("globalThis.lumyteMapsProbe.HasPresentedInputAsync()"), true,
        "Physical UI input requires committed presentation geometry; state=" + await evaluate("globalThis.lumyteMapsProbe.GetState()"));
    const bounds = await evaluate("(() => { const r = document.querySelector('canvas').getBoundingClientRect(); return { x: r.left, y: r.top, width: r.width, height: r.height }; })()");
    const start = { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
    const readCamera = async () => (await evaluate("globalThis.lumyteMapsProbe.GetCamera()")).split(",").map(Number);
    await evaluate("(() => { globalThis.__mapInputEvents = []; for (const type of ['pointerdown','pointermove','pointerup','pointercancel','wheel']) window.addEventListener(type, e => globalThis.__mapInputEvents.push({ type, x:e.clientX, y:e.clientY, buttons:e.buttons, target:e.target.id, focused:document.hasFocus(), active:document.activeElement?.id }), true); })()");
    const initialCamera = await readCamera();
    await command("Input.dispatchMouseEvent", { type: "mouseMoved", ...start });
    await command("Input.dispatchMouseEvent", { type: "mousePressed", ...start, button: "left", buttons: 1, clickCount: 1 });
    await command("Input.dispatchMouseEvent", { type: "mouseMoved", x: start.x + 40, y: start.y + 20, buttons: 1 });
    await command("Input.dispatchMouseEvent", { type: "mouseReleased", x: start.x + 40, y: start.y + 20, button: "left", buttons: 0, clickCount: 1 });
    async function cameraUntil(predicate) {
        const limit = Date.now() + 10000;
        while (Date.now() < limit) {
            const camera = await readCamera();
            if (predicate(camera)) return camera;
            await delay(25);
        }
        assert.fail("Actual input did not update camera: " + await readCamera() + "; state=" + await evaluate("globalThis.lumyteMapsProbe.GetState()") + "; DOM=" + JSON.stringify(await evaluate("globalThis.__mapInputEvents")) + "; errors=" + JSON.stringify(errors) + "; progress=" + JSON.stringify(progress.slice(-10)));
    }
    const panned = await cameraUntil(camera => Math.abs(camera[0] - initialCamera[0] + 28.125) < 1e-8);
    const wheelPoint = { x: start.x + 80, y: start.y - 40 };
    await command("Input.dispatchMouseEvent", { type: "mouseWheel", ...wheelPoint, deltaX: 0, deltaY: -120 });
    const zoomed = await cameraUntil(camera => camera[2] === 0.25);
    const project = camera => [(camera[0] + 180) / 360, (1 - Math.asinh(Math.tan(camera[1] * Math.PI / 180)) / Math.PI) / 2];
    const before = project(panned);
    const after = project(zoomed);
    const world = 512 * 2 ** zoomed[2];
    assert.ok(Math.abs(before[0] + 80 / 512 - after[0] - 80 / world) < 1e-10, "Wheel longitude anchor");
    assert.ok(Math.abs(before[1] - 40 / 512 - after[1] + 40 / world) < 1e-10, "Wheel latitude anchor");
    console.log("Actual CDP/DOM pan and wheel through Browser acquisition, UI host, MapView passed: " + zoomed);
    await command("Input.dispatchMouseEvent", { type: "mouseWheel", ...wheelPoint, deltaX: 0, deltaY: -480 });
    await cameraUntil(camera => camera[2] === 1.25);
    const adoptionDeadline = Date.now() + 10000;
    while (Date.now() < adoptionDeadline && await evaluate("globalThis.lumyteMapsProbe.GetPublishedTiles()") < 5) await delay(25);
    assert.equal(await evaluate("globalThis.lumyteMapsProbe.GetPublishedTiles()"), 5, "Integer source zoom must adopt the four local z1 tiles, not reuse z0 as fallback");
    assert.ok(await evaluate("globalThis.lumyteMapsProbe.RecordUiAsync()") >= 5);
    console.log("Browser source zoom 0 -> 1: four newly demanded local tiles adopted and recorded.");
    await evaluate("globalThis.lumyteMapsProbe.RequestStop()");
    while (Date.now() < deadline && !await evaluate("globalThis.lumyteMapsFinished")) {
        assert.deepEqual(errors, [], "Browser cleanup failed");
        await delay(50);
    }
    assert.equal(await evaluate("globalThis.lumyteMapsFinished"), true, "Content must stop and release cleanly");
    assert.deepEqual(errors, [], "Browser reported errors: " + progress.join("\n"));
    if (requiresSoftwareWebGpu()) {
        const audit = await command("Runtime.evaluate", { expression: "globalThis.__lumyteBrowserGpuAudit", returnByValue: true });
        verifySoftwareWebGpu(audit.result.value);
    }
    console.log("Maps Browser execution smoke passed: in-memory source, actual Resources decode/adoption, mounted MapView direct/retained recording and orderly stop. GPU PNG only when explicitly requested.");
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

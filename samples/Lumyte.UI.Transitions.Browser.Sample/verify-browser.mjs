import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { extname, resolve, sep } from "node:path";
import { inflateSync } from "node:zlib";
import { setTimeout as delay } from "node:timers/promises";

const root = resolve(process.argv[2] ?? "samples/Lumyte.UI.Transitions.Browser.Sample/bin/Release/net10.0/publish/wwwroot");
const sequential = process.argv.includes("--sequential");
const sameContent = process.argv.includes("--same-content");
const executable = process.env.CHROME ?? "chromium";
const profile = await mkdtemp(resolve(tmpdir(), "lumyte-navigation-"));
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
    const launchDeadline = Date.now() + 120000;
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
    await command("Emulation.setFocusEmulationEnabled", { enabled: true });
    await command("Page.bringToFront");
    const throttle = Number(process.env.LUMYTE_BROWSER_CPU_THROTTLE ?? 1);
    assert.ok(Number.isFinite(throttle) && throttle >= 1 && throttle <= 20, "CPU throttle must be between 1 and 20");
    if (throttle > 1) await command("Emulation.setCPUThrottlingRate", { rate: throttle });
    await command("Page.navigate", { url: `http://127.0.0.1:${server.address().port}${sequential ? "/?sequential" : sameContent ? "/?same-content" : ""}` });
    async function evaluate(expression) {
        const response = await command("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true });
        if (response.exceptionDetails) {
            const state = await command("Runtime.evaluate", { expression: "globalThis.lumyteApplicationProbe?.GetState()", returnByValue: true });
            throw new Error(progress.join("\n") + "\n" + state.result.value + "\n" + JSON.stringify(response.exceptionDetails));
        }
        return response.result.value;
    }
    const deadline = Date.now() + 120000;
    while (Date.now() < deadline) {
        assert.deepEqual(errors, [], "Browser startup failed");
        assert.equal(await evaluate("globalThis.lumyteApplicationError"), undefined);
        if (await evaluate("globalThis.lumyteApplicationProbe?.IsPresented() === true")) break;
        await delay(100);
    }
    const probe = "globalThis.lumyteApplicationProbe";
    async function png() { return Buffer.from(await evaluate(`${probe}.CapturePngAsync()`), "base64"); }
    function pixel(buffer, x, y) {
        const chunks = [];
        for (let offset = 8; offset < buffer.length;) {
            const length = buffer.readUInt32BE(offset);
            if (buffer.subarray(offset + 4, offset + 8).toString() === "IDAT") chunks.push(buffer.subarray(offset + 8, offset + 8 + length));
            offset += length + 12;
        }
        const data = inflateSync(Buffer.concat(chunks));
        const row = y * (buffer.readUInt32BE(16) * 4 + 1);
        assert.equal(data[row], 0);
        return [...data.subarray(row + 1 + x * 4, row + 5 + x * 4)];
    }
    function color(buffer, x, y, expected) {
        const actual = pixel(buffer, x, y);
        assert.ok(actual.every((v, i) => Math.abs(v - expected[i]) <= 3), `Pixel ${x},${y}: ${actual}, expected ${expected}`);
    }
    async function liveColor(x, y, expected) {
        for (let attempt = 0; attempt < 8; attempt++) {
            const current = await png();
            if (pixel(current, x, y).every((value, index) => Math.abs(value - expected[index]) <= 3)) return;
        }
        color(await png(), x, y, expected);
    }
    async function until(expression) {
        const end = Date.now() + 45000;
        while (Date.now() < end) {
            assert.deepEqual(errors, [], "Browser failed: " + await evaluate(`${probe}.GetState()`));
            if (await evaluate(expression)) return;
            await delay(50);
        }
        assert.fail(`Wait expired: ${expression}; ${await evaluate(`${probe}.GetState()`)}; popup=${await evaluate(`${probe}.GetPopupState()`)}; visual=${await evaluate(`${probe}.GetVisual()`)}; disposed=${await evaluate(`${probe}.GetDisposed()`)}`);
    }
    const canvasRect = JSON.parse(await evaluate("JSON.stringify(document.querySelector('canvas').getBoundingClientRect().toJSON())"));
    async function click(id) {
        const point = JSON.parse(await evaluate(`${probe}.PointAsync('${id}')`));
        for (const type of ["mousePressed", "mouseReleased"]) {
            await command("Input.dispatchMouseEvent", { type, x: canvasRect.x + point[0], y: canvasRect.y + point[1], button: "left", buttons: type === "mousePressed" ? 1 : 0, clickCount: 1 });
        }
    }
    console.log("WebGPU host ready");
    if (!sameContent) {
        await png();
        const initial = await png();
        color(initial, 20, 20, [255, 255, 255, 255]);
        assert.equal(await evaluate(`${probe}.GetEntered()`), 0);
        await evaluate(`${probe}.AdvanceAsync(0.5)`);
        color(await png(), 20, 20, [188, 188, 255, 255]); // one flattened group, not separate child alpha
        await evaluate(`${probe}.SetGroupGreenAsync(true)`);
        await liveColor(20, 20, [188, 255, 188, 255]);
        assert.equal(await evaluate(`${probe}.GetEntered()`), 0, "A content change must not finish or restart the fade");
        await evaluate(`${probe}.SetGroupGreenAsync(false)`);
        await liveColor(20, 20, [188, 188, 255, 255]);
        await evaluate(`${probe}.AdvanceAsync(0.5)`);
        color(await png(), 20, 20, [0, 0, 255, 255]);
        await until(`${probe}.GetEntered() === 1`);
        // An actual user click activates the Browser owner before opening a menu.
        for (const type of ["mousePressed", "mouseReleased"]) {
            await command("Input.dispatchMouseEvent", { type, x: canvasRect.x + 190, y: canvasRect.y + 10, button: "left", buttons: type === "mousePressed" ? 1 : 0, clickCount: 1 });
        }
        await png();
        await png();
        console.log("Entry presented; opening Hosted popup");
        await evaluate(`${probe}.PopupAsync(true)`);
        await until(`${probe}.GetPopupState() === 'Open'`);
        console.log("Hosted popup open");
        await png();
        const popup = JSON.parse(await evaluate(`${probe}.PopupPoint()`));
        color(await png(), popup[0], popup[1], [0, 0, 255, 255]);
        await evaluate(`${probe}.PopupAsync(false)`);
        await png();
        await png();
        assert.equal(await evaluate(`${probe}.GetPopupClosed()`), 0, "Popup cleanup cannot precede exit");
        await evaluate(`${probe}.AdvanceAsync(0.5)`);
        color(await png(), popup[0], popup[1], [188, 188, 255, 255]);
        await evaluate(`${probe}.AdvanceAsync(0.5)`);
        await png();
        await until(`${probe}.GetPopupClosed() === 1 && ${probe}.GetPopupCount() === 0`);
        console.log("Hosted popup exit and cleanup completed");
        await evaluate(`${probe}.HideAsync()`);
        await png();
        await png();
        await evaluate(`${probe}.AdvanceAsync(0.5)`);
        color(await png(), 20, 20, [188, 188, 255, 255]);
        await evaluate(`${probe}.AdvanceAsync(0.5)`);
        await png();
        await until(`${probe}.GetExited() === 1`);
    } else {
        await png();
        await until(`${probe}.GetEntered() === 1`);
        await evaluate(`${probe}.HideAsync()`);
        await png();
        await until(`${probe}.GetExited() === 1`);
    }
    const url = await evaluate("location.href");
    const history = await evaluate("window.history.length");
    console.log("Element exit presented; requesting connected page");
    await click("next");
    await until(`${probe}.GetRoute() === '/details' && ${probe}.GetState().includes('Animating')`);
    assert.equal(await evaluate(`${probe}.GetDisposed()`), 0, "The old DI scope survives the flight");
    assert.equal(await evaluate(`${probe}.IsGatedAsync()`), true);
    await png();
    await evaluate(`${probe}.AdvanceAsync(0.5)`);
    const middle = await png();
    color(middle, 70, 40, sameContent ? [255, 188, 188, 255] : [225, 188, 225, 255]); // isolated half-alpha endpoints over the new white background
    if (sequential) color(middle, 20, 20, [188, 255, 188, 255]); // old page fading before new entry
    color(middle, 150, 60, [255, 255, 255, 255]); // no normal target duplicate
    assert.equal(await evaluate(`${probe}.GetVisual()`), "pending");
    await evaluate(`${probe}.SetPageGreenAsync()`);
    if (!sequential) await liveColor(20, 20, [0, 255, 0, 255]);
    assert.equal(await evaluate(`${probe}.GetVisual()`), "pending", "Target content updates must keep the flight alive");
    assert.equal(await evaluate(`${probe}.GetDisposed()`), 0);
    await click("quit"); // ordinary input is discarded while the page is gated
    assert.equal(await evaluate("globalThis.lumyteApplicationFinished"), false);
    await evaluate(`${probe}.AdvanceAsync(0.5)`);
    color(await png(), 120, 60, sameContent ? [255, 188, 188, 255] : [188, 188, 255, 255]);
    if (sequential) {
        assert.equal(await evaluate(`${probe}.GetVisual()`), "pending");
        await evaluate(`${probe}.AdvanceAsync(0.5)`);
        color(await png(), 20, 20, [188, 255, 188, 255]); // live updated green page enters only after old exit
        await evaluate(`${probe}.AdvanceAsync(0.5)`);
        color(await png(), 20, 20, [0, 255, 0, 255]);
    }
    await until(`${probe}.GetVisual() === 'Completed;completed' && ${probe}.GetDisposed() === 1 && ${probe}.GetState().includes('Idle')`);
    await evaluate(`${probe}.BackAsync()`);
    await until(`${probe}.GetRoute() === '/' && ${probe}.GetState().includes('Animating')`);
    await png();
    await evaluate(`${probe}.AdvanceAsync(${sequential ? 2 : 1})`);
    await png();
    await until(`${probe}.GetVisual() === 'Completed;completed' && ${probe}.GetDisposed() === 2 && ${probe}.GetState().includes('Idle')`);
    await evaluate(`${probe}.ForwardAsync()`);
    await until(`${probe}.GetRoute() === '/details' && ${probe}.GetVisual() === 'Skipped;skipped-by-request' && ${probe}.GetDisposed() === 3`);
    assert.equal(await evaluate("location.href"), url);
    assert.equal(await evaluate("window.history.length"), history);
    await click("quit");
    await until("globalThis.lumyteApplicationFinished === true");
    assert.equal(await evaluate(`${probe}.GetDisposed()`), 4);
    assert.deepEqual(errors, []);
    await writeFile(resolve(tmpdir(), "lumyte-ui-connected-mid.png"), middle);
    console.log(`${sequential ? "ExitThenEnter" : sameContent ? "One-pixel-budget live shared layer" : "Connected-only"} UI transitions WebGPU passed: live content during fades/flight, group alpha, Hosted popup exit/drain, isolated crossfade or allocation-free shared movement, bounds, input gate, DI retention, back/forward/skip and shutdown.`);
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

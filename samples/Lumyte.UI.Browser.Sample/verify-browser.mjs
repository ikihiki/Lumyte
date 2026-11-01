import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdtemp, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { extname, resolve, sep } from "node:path";
import { setTimeout as delay } from "node:timers/promises";

const root = resolve(process.argv[2] ?? "samples/Lumyte.UI.Browser.Sample/bin/Release/net10.0/publish/wwwroot");
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
    await readFile(resolve(root, "index.html"));
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
    await command("Page.navigate", { url: `http://127.0.0.1:${server.address().port}/${process.env.LUMYTE_UI_INPUT_ONLY ? "?input-only" : ""}` });
    async function evaluate(expression) {
        const response = await command("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true });
        if (response.exceptionDetails) throw new Error(response.exceptionDetails.exception?.description ?? response.exceptionDetails.text);
        return response.result.value;
    }
    async function state() { return JSON.parse(await evaluate("JSON.stringify(globalThis.lumyteUi?.GetState()?.split(','))") ?? "null"); }
    async function wait(predicate, label) {
        const deadline = Date.now() + 60000;
        while (Date.now() < deadline) {
            const current = await state();
            if (current && predicate(current)) return current;
            if (errors.length) throw new Error(`${errors.join("\n")}\n${progress.join("\n")}\n${diagnostics}`);
            await delay(30);
        }
        throw new Error(`Timed out: ${label} state=${await state()} trace=${await evaluate("JSON.stringify(globalThis.uiTrace)")}\n${progress.join("\n")}\n${errors.join("\n")}`);
    }
    await wait(value => Number(value[1]) >= 2, "initial GPU frames");
    await evaluate("globalThis.uiTrace=[]; for(const type of ['pointerdown','pointermove','pointerup','pointercancel','lostpointercapture']) document.querySelector('canvas').addEventListener(type,e=>uiTrace.push([type,e.buttons,e.clientX,e.clientY]));");
    const rect = JSON.parse(await evaluate("JSON.stringify(document.querySelector('canvas').getBoundingClientRect().toJSON())"));
    async function mouse(type, x, y, buttons) {
        await command("Input.dispatchMouseEvent", { type, x: rect.x + x, y: rect.y + y, button: type === "mouseMoved" && !buttons ? "none" : "left", buttons, clickCount: type === "mouseMoved" ? 0 : 1 });
    }
    await mouse("mouseMoved", 20, 20, 0);
    await mouse("mousePressed", 20, 20, 1);
    await wait(value => value[2] === "True", "main press");
    await mouse("mouseMoved", 550, 20, 1);
    await delay(50);
    await mouse("mouseReleased", 550, 20, 0);
    await wait(value => value[2] === "False", "outside release");
    assert.equal((await state())[0], "0", "Outside release must not click.");
    await mouse("mousePressed", 20, 20, 1);
    await mouse("mouseReleased", 20, 20, 0);
    await wait(value => value[0] === "1", "main click");
    await mouse("mousePressed", 240, 120, 1);
    await mouse("mouseReleased", 240, 120, 0);
    await wait(value => value[0] === "2", "hosted click");
    await command("Input.dispatchKeyEvent", { type: "keyDown", code: "Space", key: " ", windowsVirtualKeyCode: 32 });
    await wait(value => value[3] === "True", "focused hosted key press");
    await command("Input.dispatchKeyEvent", { type: "keyUp", code: "Space", key: " ", windowsVirtualKeyCode: 32 });
    await wait(value => value[0] === "3", "focused hosted key activation");
    await command("Emulation.setDeviceMetricsOverride", { width: 1000, height: 700, deviceScaleFactor: 2, mobile: false });
    await delay(100);
    await mouse("mousePressed", 20, 20, 1);
    await mouse("mouseReleased", 20, 20, 0);
    await wait(value => value[0] === "4", "DPI-scaled input");
    if (process.env.LUMYTE_SCREENSHOT) {
        const screenshot = await command("Page.captureScreenshot", { format: "png" });
        await writeFile(process.env.LUMYTE_SCREENSHOT, Buffer.from(screenshot.data, "base64"));
    }
    assert.deepEqual(errors, [], "Browser reported errors.");
    await evaluate("document.querySelector('button').click()");
    await delay(200);
    assert.deepEqual(errors, [], "Shutdown reported errors.");
    console.log(`Browser UI ${process.env.LUMYTE_UI_INPUT_ONLY ? "input/CPU drawing" : "WebGPU"} verification passed: DOM capture, outside release, Hosted, keyboard activation, DPI x2 and shutdown.`);

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

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

if (process.env.LUMYTE_UI_ORIGIN_FILE) {
    await writeFile(process.env.LUMYTE_UI_ORIGIN_FILE, `http://127.0.0.1:${server.address().port}`);
}
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
    await command("Page.enable");
    await command("Emulation.setDeviceMetricsOverride", { width: 1000, height: 700, deviceScaleFactor: 1, mobile: false });
    await command("Page.bringToFront");
    const query = new URLSearchParams();
    if (process.env.LUMYTE_UI_DIAGNOSTICS) query.set("diagnostics", "");
    if (process.env.LUMYTE_UI_POPUPS) query.set("popups", "");
    if (process.env.LUMYTE_UI_AUTOMATION) query.set("automation", "");
    if (process.env.LUMYTE_UI_INPUT_ONLY) query.set("input-only", "");
    if (process.env.LUMYTE_UI_RENDER_GRAPH) query.set("render-graph", "");
    if (process.env.LUMYTE_UI_RESOURCES) query.set("resources", "");
    if (process.env.LUMYTE_UI_DIAGNOSTICS) {
        let ready = false;
        for (let attempt = 0; attempt < 200 && !ready; attempt++) {
            try {
                const response = await fetch(`${process.env.LUMYTE_DIAGNOSTIC_SERVICE}diagnostics/v1/sessions`, { headers: { Authorization: `Bearer ${process.env.LUMYTE_DIAGNOSTICS_OPERATOR_TOKEN}` } });
                ready = response.ok;
            } catch {}
            if (!ready) await delay(50);
        }
        assert.ok(ready, "Diagnostic service did not start.");
        const bootstrap = `globalThis.lumyteDiagnosticService=${JSON.stringify(process.env.LUMYTE_DIAGNOSTIC_SERVICE)};globalThis.lumyteDiagnosticGameToken=${JSON.stringify(process.env.LUMYTE_DIAGNOSTICS_GAME_TOKEN)};`;
        await command("Page.addScriptToEvaluateOnNewDocument", { source: bootstrap });
    }
    await command("Page.navigate", { url: `http://127.0.0.1:${server.address().port}/?${query}` });
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
    const initialClicks = process.env.LUMYTE_UI_AUTOMATION ? 2 : 0;
    assert.equal(Number((await state())[0]), initialClicks);
    if (process.env.LUMYTE_UI_AUTOMATION) assert.ok(progress.some(line => line.includes("UI automation passed:")));
    if (process.env.LUMYTE_UI_POPUPS) {
        const rect = JSON.parse(await evaluate("JSON.stringify(document.querySelector('canvas').getBoundingClientRect().toJSON())"));
        async function pointer(type, x, y, button = "left") {
            await command("Input.dispatchMouseEvent", { type, x: rect.x + x, y: rect.y + y, button, buttons: type === "mousePressed" ? button === "right" ? 2 : 1 : 0, clickCount: 1 });
        }
        async function click(x, y) { await pointer("mousePressed", x, y); await pointer("mouseReleased", x, y); }
        async function popupState(predicate, label) {
            for (let i = 0; i < 500; i++) {
                const value = (await evaluate("globalThis.lumyteUi.GetPopupState()")).split(",");
                if (predicate(value)) return value;
                assert.deepEqual(errors, [], label);
                await delay(20);
            }
            throw new Error(`Popup timed out: ${label}: ${await evaluate("globalThis.lumyteUi.GetPopupState()")}`);
        }
        await popupState(v => v[0] === "True", "owner registration");
        await click(20, 20); await wait(v => Number(v[0]) === 1, "owner click");
        await pointer("mousePressed", 20, 20, "right"); await pointer("mouseReleased", 20, 20, "right");
        await popupState(v => v[1] === "Open", "context menu");
        await click(20, 90); await popupState(v => Number(v[2]) === 1, "popup click");
        await command("Input.dispatchKeyEvent", { type: "keyDown", code: "Enter", key: "Enter", windowsVirtualKeyCode: 13 });
        await command("Input.dispatchKeyEvent", { type: "keyUp", code: "Enter", key: "Enter", windowsVirtualKeyCode: 13 });
        await popupState(v => Number(v[2]) === 2, "popup Enter");
        await click(20, 120); await popupState(v => Number(v[3]) === 2 && Number(v[4]) === 2, "submenu");
        await click(190, 120); await popupState(v => Number(v[2]) === 3, "submenu click");
        for (let count = 1; count >= 0; count--) {
            await command("Input.dispatchKeyEvent", { type: "keyDown", code: "Escape", key: "Escape", windowsVirtualKeyCode: 27 });
            await command("Input.dispatchKeyEvent", { type: "keyUp", code: "Escape", key: "Escape", windowsVirtualKeyCode: 27 });
            await popupState(v => Number(v[3]) === count, "Escape depth");
        }
        await evaluate("globalThis.lumyteUi.OpenPopup(false)"); await popupState(v => v[1] === "Open", "reopen");
        await click(20, 20); await popupState(v => Number(v[3]) === 0, "inside-canvas outside press");
        assert.equal(Number((await state())[0]), 1, "Outside press must not click owner.");
        await evaluate("globalThis.lumyteUi.OpenPopup(false); globalThis.outsideClicks = 0; const b = document.createElement('button'); b.id = 'outside'; b.textContent = 'Outside'; b.onclick = () => outsideClicks++; b.style.cssText = 'position:fixed;left:600px;top:100px;width:100px;height:40px'; document.body.append(b)");
        await popupState(v => v[1] === "Open", "reopen for document observer");
        await command("Input.dispatchMouseEvent", { type: "mousePressed", x: 610, y: 110, button: "left", buttons: 1, clickCount: 1 });
        await command("Input.dispatchMouseEvent", { type: "mouseReleased", x: 610, y: 110, button: "left", buttons: 0, clickCount: 1 });
        await popupState(v => Number(v[3]) === 0, "document outside observer");
        assert.equal(await evaluate("globalThis.outsideClicks"), 1, "DOM input must still reach its element.");
        await command("Emulation.setDeviceMetricsOverride", { width: 1000, height: 700, deviceScaleFactor: 2, mobile: false });
        await evaluate("document.querySelector('canvas').focus(); globalThis.lumyteUi.OpenPopup(false)"); await popupState(v => v[1] === "Open", "DPI reopen");
        await evaluate("document.querySelector('canvas').style.width = '320px'; document.querySelector('canvas').style.height = '240px'");
        await delay(150); await popupState(v => v[1] === "Open", "resize");
        await evaluate("document.querySelector('canvas').blur()"); await popupState(v => Number(v[3]) === 0, "blur");
        assert.equal(await evaluate("document.querySelectorAll('canvas').length"), 1, "Hosted must not create another canvas.");
        await evaluate("document.querySelector('canvas').remove()"); await delay(200);
    } else if (process.env.LUMYTE_UI_DIAGNOSTICS) {
        const operator = spawn(process.env.DOTNET ?? "dotnet", [
            "samples/Lumyte.Diagnostics.UI.Automation.Operator.Sample/bin/Release/net10.0/Lumyte.Diagnostics.UI.Automation.Operator.Sample.dll",
            process.env.LUMYTE_DIAGNOSTIC_SERVICE,
        ], { stdio: ["ignore", "pipe", "pipe"], env: { ...process.env, LUMYTE_UI_CLOSE_SAMPLE: "0" } });
        let output = "";
        operator.stdout.on("data", data => { output += data; });
        operator.stderr.on("data", data => { output += data; });
        const code = await new Promise((resolve, reject) => {
            operator.once("error", reject);
            operator.once("exit", resolve);
        });
        assert.equal(code, 0, output);
        assert.ok(output.includes("Remote UI automation passed:"), output);
        await wait(value => Number(value[0]) === 2, "remote saves");
    } else {
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
    assert.equal(Number((await state())[0]), initialClicks, "Outside release must not click.");
    await mouse("mousePressed", 20, 20, 1);
    await mouse("mouseReleased", 20, 20, 0);
    await wait(value => Number(value[0]) === initialClicks + 1, "main click");
    await mouse("mousePressed", 240, 120, 1);
    await mouse("mouseReleased", 240, 120, 0);
    await wait(value => Number(value[0]) === initialClicks + 2, "hosted click");
    await command("Input.dispatchKeyEvent", { type: "keyDown", code: "Space", key: " ", windowsVirtualKeyCode: 32 });
    await wait(value => value[3] === "True", "focused hosted key press");
    await command("Input.dispatchKeyEvent", { type: "keyUp", code: "Space", key: " ", windowsVirtualKeyCode: 32 });
    await wait(value => Number(value[0]) === initialClicks + 3, "focused hosted key activation");
    await command("Emulation.setDeviceMetricsOverride", { width: 1000, height: 700, deviceScaleFactor: 2, mobile: false });
    await delay(100);
    await mouse("mousePressed", 20, 20, 1);
    await mouse("mouseReleased", 20, 20, 0);
    await wait(value => Number(value[0]) === initialClicks + 4, "DPI-scaled input");
    if (process.env.LUMYTE_AUTOMATION_SCREENSHOT) {
        const png = await evaluate("globalThis.lumyteAutomationPng");
        assert.ok(png, "No automation GPU PNG was captured.");
        await writeFile(process.env.LUMYTE_AUTOMATION_SCREENSHOT, Buffer.from(png, "base64"));
    }
    if (process.env.LUMYTE_SCREENSHOT) {
        const screenshot = await command("Page.captureScreenshot", { format: "png" });
        await writeFile(process.env.LUMYTE_SCREENSHOT, Buffer.from(screenshot.data, "base64"));
    }
    }
    assert.deepEqual(errors, [], "Browser reported errors.");
    await evaluate("document.querySelector('button').click()");
    await delay(200);
    assert.deepEqual(errors, [], "Shutdown reported errors.");
    console.log(process.env.LUMYTE_UI_POPUPS ? "Browser Hosted popups passed: contextmenu, submenu, Click, Enter, Escape, outside series, document observer, DPI, resize, blur, canvas removal and shutdown." : process.env.LUMYTE_UI_DIAGNOSTICS
        ? "Browser UI Diagnostics HTTP verification passed: external typed Operator, Save, Expect, Enter, count and shutdown."
        : `Browser UI ${process.env.LUMYTE_UI_INPUT_ONLY ? "input/CPU drawing" : "WebGPU"} verification passed: ${process.env.LUMYTE_UI_AUTOMATION ? "Save locator, Enter and assertions; " : ""}DOM capture, outside release, Hosted, keyboard activation, DPI x2 and shutdown.`);

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

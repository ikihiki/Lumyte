// Transport only. BenchmarkDotNet in the .NET host owns all measurement.
import { spawn } from "node:child_process";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { createServer } from "node:http";
import { createInterface } from "node:readline";
import { tmpdir } from "node:os";
import { extname, resolve, sep } from "node:path";
import { setTimeout as delay } from "node:timers/promises";

const [rootArgument, chrome, profileName] = process.argv.slice(2);
const root = resolve(rootArgument);
const repository = resolve(import.meta.dirname, "../../..");
const { installBrowserGpuAudit } = await import(resolve(repository, "tools/graphics/browser-gpu.mjs"));
const profile = await mkdtemp(resolve(tmpdir(), "lumyte-ui-bdn-"));
let browser;
let socket;
let server;
let closing = false;
const pending = new Map();
let nextId = 0;
async function cleanup() {
    if (closing) return;
    closing = true;
    socket?.close();
    for (const operation of pending.values()) operation.reject(new Error("Transport closed"));
    pending.clear();
    if (browser && browser.exitCode === null) {
        browser.kill("SIGTERM");
        await Promise.race([new Promise(resolve => browser.once("exit", resolve)), delay(2000, undefined, { ref: false })]);
        if (browser.exitCode === null) browser.kill("SIGKILL");
    }
    server?.closeAllConnections();
    await Promise.race([new Promise(resolve => server ? server.close(resolve) : resolve()), delay(2000)]);
    await rm(profile, { recursive: true, force: true });
}
process.on("uncaughtException", error => { process.stderr.write(String(error)); cleanup().finally(() => process.exit(1)); });
process.on("unhandledRejection", error => { process.stderr.write(String(error)); cleanup().finally(() => process.exit(1)); });
process.on("SIGTERM", () => { cleanup().finally(() => process.exit(0)); });
process.on("SIGINT", () => { cleanup().finally(() => process.exit(0)); });
function command(method, params = {}) {
    const id = ++nextId;
    return new Promise((resolve, reject) => {
        const timeout = setTimeout(() => { pending.delete(id); reject(new Error("CDP timeout: " + method)); }, 45000);
        pending.set(id, {
            resolve: value => { clearTimeout(timeout); resolve(value); },
            reject: error => { clearTimeout(timeout); reject(error); },
        });
        socket.send(JSON.stringify({ id, method, params }));
    });
}
async function evaluate(expression) {
    const result = await command("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
function reply(value) { process.stdout.write(JSON.stringify(value) + "\n"); }
try {
    if (process.env.LUMYTE_UI_BENCHMARK_GPU !== "1") throw new Error("SKIP: GPU opt-in is required");
    await readFile(resolve(root, "index.html"));
    const types = { ".html": "text/html", ".js": "text/javascript", ".json": "application/json", ".wasm": "application/wasm" };
    server = createServer(async (request, response) => {
        try {
            const pathname = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
            const path = resolve(root, "." + (pathname === "/" ? "/index.html" : pathname));
            if (!path.startsWith(root + sep)) { response.writeHead(403).end(); return; }
            const contents = await readFile(path);
            response.writeHead(200, { "Content-Type": types[extname(path)] ?? "application/octet-stream" }).end(contents);
        } catch { response.writeHead(404).end(); }
    });
    await new Promise((resolve, reject) => { server.once("error", reject); server.listen(0, "127.0.0.1", resolve); });
    browser = spawn(chrome, ["--headless=new", "--disable-background-timer-throttling", "--disable-renderer-backgrounding", "--disable-backgrounding-occluded-windows", "--no-sandbox", "--disable-dev-shm-usage", "--enable-unsafe-webgpu", "--enable-dawn-features=allow_unsafe_apis", "--enable-features=Vulkan", "--use-vulkan=native", "--use-angle=vulkan", "--ignore-gpu-blocklist", "--remote-debugging-port=0", "--user-data-dir=" + profile, "about:blank"], { stdio: ["ignore", "ignore", "pipe"] });
    let diagnostics = "";
    let launchError;
    browser.on("error", error => { launchError = error; });
    browser.stderr.on("data", data => { diagnostics = (diagnostics + data).slice(-32000); });
    let port;
    for (let attempt = 0; attempt < 600; attempt++) {
        if (launchError) throw launchError;
        port = /DevTools listening on ws:[/][/]127[.]0[.]0[.]1:([0-9]+)/.exec(diagnostics)?.[1];
        if (port) break;
        if (browser.exitCode !== null) throw new Error(diagnostics);
        await delay(100);
    }
    if (!port) throw new Error("Chrome launch deadline: " + diagnostics);
    const pages = await (await fetch("http://127.0.0.1:" + port + "/json/list", { signal: AbortSignal.timeout(10000) })).json();
    socket = new WebSocket(pages.find(page => page.type === "page").webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.addEventListener("open", resolve, { once: true }); socket.addEventListener("error", reject, { once: true }); setTimeout(() => reject(new Error("WebSocket deadline")), 10000).unref(); });
    socket.addEventListener("message", event => {
        const message = JSON.parse(event.data);
        const operation = pending.get(message.id);
        if (!operation) return;
        pending.delete(message.id);
        if (message.error) operation.reject(new Error(JSON.stringify(message.error))); else operation.resolve(message.result);
    });
    await command("Page.enable");
    await command("Page.addScriptToEvaluateOnNewDocument", { source: "(" + installBrowserGpuAudit.toString() + ")();" });
    await command("Page.navigate", { url: "http://127.0.0.1:" + server.address().port + "/?" + profileName });
    let ready = false;
    for (let attempt = 0; attempt < 1200; attempt++) {
        const state = await evaluate("({ready:globalThis.lumyteBenchmark?.IsReady(),error:globalThis.lumyteBenchmarkError || globalThis.lumyteBenchmark?.GetFailure?.()})");
        if (state.error) throw new Error(state.error);
        if (attempt === 1199) process.stderr.write("Startup state: " + JSON.stringify(state) + "\n");
        if (state.ready) { ready = true; break; }
        await delay(100);
    }
    if (!ready) throw new Error("Application startup deadline: " + diagnostics);
    const audit = await evaluate("globalThis.__lumyteBrowserGpuAudit");
    process.stderr.write("Application adapter: " + JSON.stringify(audit) + "\n");
    if (!audit?.requests?.length || audit.requests.some(request => !request.adapter || request.adapter.isFallbackAdapter !== false || request.adapter.vendor !== "amd" || /swiftshader|llvmpipe/i.test(JSON.stringify(request.adapter)))) throw new Error("Actual application hardware adapter rejected: " + JSON.stringify(audit));
    // Chrome privacy-redacts adapter device/description. Join the actual app
    // AMD/nonfallback observation with this owned browser's CDP device inventory.
    const version = await (await fetch("http://127.0.0.1:" + port + "/json/version", { signal: AbortSignal.timeout(10000) })).json();
    const browserSocket = new WebSocket(version.webSocketDebuggerUrl);
    const hardware = await new Promise((resolve, reject) => {
        const timeout = setTimeout(() => { browserSocket.close(); reject(new Error("Hardware identity deadline")); }, 10000);
        browserSocket.addEventListener("open", () => browserSocket.send(JSON.stringify({ id: 1, method: "SystemInfo.getInfo" })), { once: true });
        browserSocket.addEventListener("error", reject, { once: true });
        browserSocket.addEventListener("message", event => {
            const value = JSON.parse(event.data);
            if (value.id !== 1) return;
            clearTimeout(timeout);
            browserSocket.close();
            if (value.error) reject(new Error(JSON.stringify(value.error))); else resolve(value.result.gpu);
        });
    });
    const hardwareAdapters = hardware.devices.filter(device => device.vendorId !== 65535 && !/swiftshader|llvmpipe/i.test(device.deviceString));
    if (hardwareAdapters.length !== 1 || hardwareAdapters[0].vendorId !== 4098 || !/8060S/i.test(hardwareAdapters[0].deviceString)) throw new Error("Owned browser is not uniquely Radeon 8060S: " + JSON.stringify(hardware));
    audit.browserHardware = hardware;
    process.stderr.write("Browser hardware: " + JSON.stringify(hardware.devices) + "\n");
    await evaluate("globalThis.lumyteBenchmark.AdvanceAsync()");
    reply({ ready: true, audit });
    const input = createInterface({ input: process.stdin });
    for await (const line of input) {
        const request = JSON.parse(line);
        if (request.operation === "stop") {
            await evaluate("globalThis.lumyteBenchmark.StopAsync()");
            reply({ stopped: true });
            break;
        }
        if (request.operation === "frame") reply({ frame: await evaluate("globalThis.lumyteBenchmark.AdvanceAsync()") });
        else if (request.operation === "capture") reply({ png: await evaluate("globalThis.lumyteBenchmark.CaptureAsync()") });
        else if (request.operation === "oracle") reply({ oracle: await evaluate(
            "(async()=>{const png=await globalThis.lumyteBenchmark.CaptureAsync();const image=await createImageBitmap(await(await fetch('data:image/png;base64,'+png)).blob());const canvas=new OffscreenCanvas(image.width,image.height);const context=canvas.getContext('2d');context.drawImage(image,0,0);const points=[[5,5],[25,5],[405,5],[5,505]];return points.map(([x,y])=>Array.from(context.getImageData(x,y,1,1).data));})()") });
        else throw new Error("Unknown operation");
    }
} catch (error) {
    reply({ error: String(error) });
    process.exitCode = 1;
} finally { await cleanup(); process.exit(process.exitCode ?? 0); }

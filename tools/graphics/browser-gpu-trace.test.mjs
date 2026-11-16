import assert from "node:assert/strict";
import test from "node:test";
import vm from "node:vm";
import { readFile } from "node:fs/promises";
import { installBrowserGpuAudit } from "./browser-gpu.mjs";
import { browserGpuTraceEnabled, installBrowserGpuTrace } from "./browser-gpu-trace.mjs";

function fixture() {
    const logs = [];
    const calls = [];
    const descriptor = {};
    const pipeline = {};
    const error = new Error("original failure");
    let mode = "success";
    const promises = [];
    function method(object, name, value, async = false) {
        object[name] = function (...args) {
            assert.equal(this, object);
            assert.equal(args.length, 1);
            assert.equal(args[0], descriptor);
            calls.push(name);
            if (mode === "throw") throw error;
            if (!async) return value;
            const promise = mode === "reject" ? Promise.reject(error) : Promise.resolve(value);
            promises.push(promise);
            return promise;
        };
    }
    const buffer = {}, queue = {}, device = { queue }, adapter = {}, gpu = {};
    method(gpu, "requestAdapter", adapter, true);
    method(adapter, "requestDevice", device, true);
    method(device, "createBuffer", buffer);
    method(device, "createRenderPipeline", pipeline);
    method(device, "createRenderPipelineAsync", pipeline, true);
    method(queue, "submit", undefined);
    method(queue, "onSubmittedWorkDone", undefined, true);
    method(buffer, "mapAsync", undefined, true);
    let tick = 0;
    const page = { navigator: { gpu }, performance: { now: () => ++tick },
        console: { info: (prefix, value) => { if (prefix === "Browser GPU trace:") logs.push(JSON.parse(value)); } } };
    return { page, logs, calls, descriptor, pipeline, error, promises, gpu, adapter, device, queue, buffer,
        mode(value) { mode = value; } };
}

async function connect(f) {
    const adapterPromise = f.gpu.requestAdapter(f.descriptor);
    assert.equal(adapterPromise, f.promises.at(-1));
    assert.equal(await adapterPromise, f.adapter);
    const devicePromise = f.adapter.requestDevice(f.descriptor);
    assert.equal(devicePromise, f.promises.at(-1));
    assert.equal(await devicePromise, f.device);
    assert.equal(f.device.createBuffer(f.descriptor), f.buffer);
}

test("trace opt-in is explicit locally and enabled by the existing hosted software gate", () => {
    assert.equal(browserGpuTraceEnabled({}), false);
    assert.equal(browserGpuTraceEnabled({ LUMYTE_BROWSER_GPU_TRACE: "0" }), false);
    assert.equal(browserGpuTraceEnabled({ LUMYTE_BROWSER_GPU_TRACE: "1" }), true);
    assert.equal(browserGpuTraceEnabled({ LUMYTE_REQUIRE_SOFTWARE_WEBGPU: "1" }), true);
});

test("serialized hook observes real app receivers, arguments, exact return/promise identities; no extra work", async () => {
    const f = fixture();
    vm.runInNewContext(`(${installBrowserGpuTrace.toString()})();`, f.page);
    assert.deepEqual(f.calls, []);
    await connect(f);
    assert.equal(f.device.createRenderPipeline(f.descriptor), f.pipeline);
    assert.equal(f.queue.submit(f.descriptor), undefined);
    for (const [object, name] of [[f.device, "createRenderPipelineAsync"], [f.queue, "onSubmittedWorkDone"], [f.buffer, "mapAsync"]]) {
        const result = object[name](f.descriptor);
        assert.equal(result, f.promises.at(-1));
        await result;
        const events = f.logs.filter(event => event.method === name);
        assert.deepEqual(events.map(event => event.status), ["start", "fulfilled"]);
        assert.ok(events[1].elapsedMs > 0);
        assert.equal(events[0].id, events[1].id);
    }
    assert.deepEqual(f.calls, ["requestAdapter", "requestDevice", "createBuffer", "createRenderPipeline", "submit", "createRenderPipelineAsync", "onSubmittedWorkDone", "mapAsync"]);
    for (const name of ["createRenderPipeline", "submit"]) assert.deepEqual(f.logs.filter(e => e.method === name).map(e => e.status), ["start", "returned"]);
});

test("original synchronous throws and asynchronous rejection objects/promises are preserved", async () => {
    const f = fixture();
    installBrowserGpuTrace(f.page);
    await connect(f);
    f.mode("throw");
    for (const [object, name] of [[f.gpu, "requestAdapter"], [f.adapter, "requestDevice"], [f.device, "createRenderPipeline"], [f.device, "createRenderPipelineAsync"], [f.queue, "submit"], [f.queue, "onSubmittedWorkDone"], [f.buffer, "mapAsync"], [f.device, "createBuffer"]]) {
        assert.throws(() => object[name](f.descriptor), error => error === f.error);
        if (name !== "createBuffer") assert.equal(f.logs.at(-1).status, "throw");
    }
    f.mode("reject");
    for (const [object, name] of [[f.gpu, "requestAdapter"], [f.adapter, "requestDevice"], [f.device, "createRenderPipelineAsync"], [f.queue, "onSubmittedWorkDone"], [f.buffer, "mapAsync"]]) {
        const promise = object[name](f.descriptor);
        assert.equal(promise, f.promises.at(-1));
        await assert.rejects(promise, error => error === f.error);
        assert.equal(f.logs.at(-1).status, "rejected");
    }
});

test("missing GPU, frozen methods, throwing metadata/console do not break app", async () => {
    assert.doesNotThrow(() => installBrowserGpuTrace({}));
    assert.doesNotThrow(() => installBrowserGpuTrace({ get navigator() { throw new Error("metadata"); } }));
    const f = fixture();
    f.page.console.info = () => { throw new Error("logging"); };
    f.page.performance.now = () => { throw new Error("clock"); };
    installBrowserGpuTrace(f.page);
    await connect(f);
    assert.equal(f.device.createRenderPipeline(f.descriptor), f.pipeline);
    const g = fixture();
    Object.freeze(g.gpu);
    installBrowserGpuTrace(g.page);
    assert.equal(await g.gpu.requestAdapter(g.descriptor), g.adapter);
});

test("logging is bounded without suppressing subsequent actual calls", async () => {
    const f = fixture();
    installBrowserGpuTrace(f.page);
    await connect(f);
    for (let i = 0; i < 1100; i++) f.queue.submit(f.descriptor);
    assert.equal(f.logs.length, 2000);
    assert.equal(f.logs.at(-1).truncated, true);
    assert.equal(f.calls.filter(name => name === "submit").length, 1100);
});

test("coexists with the pre-app adapter audit in verifier injection order", async () => {
    const f = fixture();
    vm.runInNewContext(`(${installBrowserGpuTrace.toString()})();(${installBrowserGpuAudit.toString()})();`, f.page);
    await connect(f);
    f.queue.submit(f.descriptor);
    assert.equal(f.page.__lumyteBrowserGpuAudit.requests.length, 1);
    assert.deepEqual(f.logs.filter(e => e.method === "submit").map(e => e.status), ["start", "returned"]);
});

test("Transitions injects conditionally before navigation, retains capture and known-timeout stage", async () => {
    const source = await readFile(new URL("../../samples/Lumyte.UI.Transitions.Browser.Sample/verify-browser.mjs", import.meta.url), "utf8");
    assert.match(source, /if \(traceGpu\) \{\s+await command\("Page.addScriptToEvaluateOnNewDocument"/);
    assert.ok(source.indexOf("installBrowserGpuTrace.toString()") < source.indexOf('await command("Page.navigate"'));
    assert.ok(source.includes('Buffer.from(await evaluate(`${probe}.CapturePngAsync()`), "base64")'));
    assert.match(source, /stage === "entry-midpoint-png" && failure instanceof BrowserEvaluationFailure/);
    assert.ok(!source.includes('stage = "connected-'));
});

 test("Application injects tracing before navigation and preserves failure evidence and original throw", async () => {
    const source = await readFile(new URL("../../samples/Lumyte.UI.Application.Browser.Sample/verify-browser.mjs", import.meta.url), "utf8");
    assert.ok(source.indexOf("installBrowserGpuTrace.toString()") < source.indexOf('await command("Page.navigate"'));
    for (const field of ["chromeStderr: diagnostics", "errors, progress, state", "lumyteApplicationError", "__lumyteBrowserGpuAudit", "LUMYTE_BROWSER_FAILURE_REPORT", "throw error;"]) assert.ok(source.includes(field), field);
    const probe = await readFile(new URL("../../samples/Lumyte.UI.Application.Browser.Sample/BrowserProbe.cs", import.meta.url), "utf8");
    assert.ok(probe.includes("Console.Error.WriteLine(exception.ToString());"));
    assert.ok(probe.includes("throw;"));
    assert.ok(probe.includes("TimeSpan.FromSeconds(30)"));
});

test("compute dispatch, destroy, loss and uncaptured errors are observation-only", async () => {
    const f = fixture();
    const pass = { dispatchWorkgroups: (...args) => args, dispatchWorkgroupsIndirect: (...args) => args };
    const encoder = { beginComputePass: () => pass };
    const compute = {};
    let listener, lose;
    f.device.createComputePipeline = () => compute;
    f.device.createCommandEncoder = () => encoder;
    f.device.destroy = () => compute;
    f.device.lost = new Promise(resolve => { lose = resolve; });
    f.device.addEventListener = (type, handler) => { assert.equal(type, "uncapturederror"); listener = handler; };
    installBrowserGpuTrace(f.page);
    await connect(f);
    assert.equal(f.device.createComputePipeline(), compute);
    assert.equal(f.device.createCommandEncoder(), encoder);
    assert.equal(encoder.beginComputePass(), pass);
    assert.deepEqual(pass.dispatchWorkgroups(1, 2, 3), [1, 2, 3]);
    assert.deepEqual(pass.dispatchWorkgroupsIndirect(f.buffer, 0), [f.buffer, 0]);
    listener({ error: { message: "validation" }, preventDefault() { assert.fail("must not suppress errors"); } });
    lose({ reason: "destroyed", message: "done" });
    await Promise.resolve();
    assert.equal(f.device.destroy(), compute);
    assert.ok(f.logs.some(e => e.method === "device.lost" && e.message === "done"));
    assert.ok(f.logs.some(e => e.method === "uncapturederror" && e.message === "validation"));
});

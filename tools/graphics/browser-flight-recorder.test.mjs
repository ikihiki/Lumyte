import { gzipSync, gunzipSync } from "node:zlib";
import test from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, readdir, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createBrowserFlightRecorder, browserFlightRecorderEnabled } from "./browser-flight-recorder.mjs";

function clock() {
    let at = 0, next = 0;
    const jobs = new Map();
    return { now: () => at, schedule: (action, ms) => { const id = ++next; jobs.set(id, { action, at: at + ms }); return id; },
        cancel: id => jobs.delete(id), jobs,
        tick(ms) { at += ms; for (const [id, job] of [...jobs]) if (job.at <= at) { jobs.delete(id); job.action(); } } };
}
const flush = async () => { for (let i = 0; i < 30; i++) await Promise.resolve(); };
async function fixture(t, options = {}) {
    const outputRoot = await mkdtemp(join(tmpdir(), "flight-recorder-test-"));
    t.after(() => rm(outputRoot, { recursive: true, force: true }));
    const time = clock(), calls = [], summaries = [];
    const recorder = createBrowserFlightRecorder({ enabled: true, sample: "test", mode: "sequential", outputRoot,
        emit: value => summaries.push(value), ...time, budgetMs: 100, slowMs: 5, ...options });
    const send = async (method, params, timeout) => {
        calls.push({ method, params, timeout });
        if (method === "Tracing.end") recorder.dispatch({ method: "Tracing.tracingComplete", params: { stream: "trace-stream" } });
        if (method === "IO.read") return { data: gzipSync('{"traceEvents":[{"name":"before-slow"}]}').toString("base64"), base64Encoded: true, eof: true };
        return {};
    };
    return { recorder, time, calls, summaries, send, outputRoot,
        async manifest() { const names = await readdir(outputRoot); assert.equal(names.length, 1); return JSON.parse(await readFile(join(outputRoot, names[0], "manifest.json"), "utf8")); } };
}
test("ordinary CI gate and explicit local flag only", () => {
    assert.equal(browserFlightRecorderEnabled({}), false);
    assert.equal(browserFlightRecorderEnabled({ LUMYTE_REQUIRE_SOFTWARE_WEBGPU: "1" }), true);
    assert.equal(browserFlightRecorderEnabled({ LUMYTE_BROWSER_FLIGHT_RECORDER: "1" }), true);
});
test("pre-navigation arm is idempotent; clean success closes/discards stream quietly", async t => {
    const f = await fixture(t);
    await Promise.all([f.recorder.arm(f.send), f.recorder.arm(f.send)]);
    await f.send("Page.navigate", {});
    await f.recorder.request(42, { stage: "initial" }, async () => "pixels");
    await f.recorder.finish();
    assert.deepEqual(f.calls.map(x => x.method), ["Tracing.start", "Page.navigate", "Tracing.end", "IO.close"]);
    assert.equal(f.calls[0].params.traceConfig.recordMode, "recordContinuously");
    assert.equal(f.calls[0].params.transferMode, "ReturnAsStream");
    assert.equal(f.calls[0].params.streamCompression, "gzip");
    assert.equal(f.calls[0].params.traceConfig.traceBufferSizeInKb, 2048);
    assert.deepEqual(await readdir(f.outputRoot), []);
    assert.equal(f.summaries.length, 0); assert.equal(f.time.jobs.size, 0);
});
test("slow passing PNG retains prehistory, request chronology, one dump and passed label", async t => {
    const f = await fixture(t);
    f.recorder.dispatch({ method: "Tracing.tracingComplete", params: { stream: "stale" } });
    await f.recorder.arm(f.send);
    f.recorder.dispatch({ method: "Runtime.bindingCalled", params: { name: "__lumyteCaptureDiagnosticEvent", payload: 'UI Capture trace: {"eventId":1,"name":"before","ticks":1,"frequency":1000,"data":{"requestId":9}}', executionContextId: 9 } }, { stage: "startup" });
    let done;
    const request = f.recorder.request(17, { pngIndex: 3, stage: "entry-midpoint-png" }, () => new Promise(resolve => { done = resolve; }));
    f.time.tick(5); await flush(); done("png"); await request;
    await f.recorder.finish(); await f.recorder.finish(true);
    const m = await f.manifest();
    assert.equal(m.verdict, "passed"); assert.equal(m.reason, "slow-png"); assert.equal(m.status, "saved");
    assert.ok(m.records.some(x => x.kind === "binding" && x.executionContextId === 9));
    assert.ok(m.records.some(x => x.kind === "png-slow" && x.requestId === 17 && x.pngIndex === 3));
    assert.ok(m.records.some(x => x.kind === "png-fulfilled"));
    assert.equal(f.calls.filter(x => x.method === "Tracing.end").length, 1);
    assert.equal(f.summaries.length, 0); assert.equal(f.time.jobs.size, 0);
});
test("failure preserves original exception and bounded metadata ring", async t => {
    const f = await fixture(t, { historyBytes: 600 });
    await f.recorder.arm(f.send);
    for (let i = 0; i < 40; i++) f.recorder.record({ kind: "counter", index: i });
    const original = new Error("original PNG verdict");
    await assert.rejects(f.recorder.request(7, {}, async () => { throw original; }), error => error === original);
    await f.recorder.finish(true);
    const m = await f.manifest();
    assert.equal(m.verdict, "failed"); assert.equal(f.summaries.length, 1); assert.ok(m.dropped > 0); assert.ok(m.historyBytes <= 600);
    assert.ok(m.records.some(x => x.kind === "png-rejected"));
    assert.equal(f.time.jobs.size, 0);
});
test("IO stream size cap and close, even base64 chunks", async t => {
    const f = await fixture(t, { byteLimit: 32 });
    await f.recorder.arm(async (...args) => args[0] === "IO.read" ? { data: Buffer.alloc(100).toString("base64"), base64Encoded: true, eof: false } : f.send(...args));
    await f.recorder.finish(true);
    const m = await f.manifest();
    assert.equal(m.incomplete, true);
    assert.equal(m.bytes, 32); assert.equal(m.status, "size-limit"); assert.equal(m.truncated, true);
    assert.ok(f.calls.some(x => x.method === "IO.close"));
});
test("unsupported tracing cannot fail the original workload", async t => {
    const f = await fixture(t);
    await f.recorder.arm(async () => { throw new Error("unsupported category"); });
    assert.equal(await f.recorder.request(1, {}, async () => 99), 99);
    await f.recorder.finish(true);
    const m = await f.manifest(); assert.equal(m.status, "trace-unavailable"); assert.equal(m.verdict, "failed");
    assert.equal(f.time.jobs.size, 0);
});
test("hung Tracing.end uses independent budget and received-event fallback", async t => {
    const f = await fixture(t);
    await f.recorder.arm(async (...args) => args[0] === "Tracing.end" ? new Promise(() => {}) : f.send(...args));
    const finish = f.recorder.finish(true); await flush(); f.time.tick(101); await finish;
    const m = await f.manifest(); assert.equal(m.status, "incomplete-browser-or-diagnostic-timeout");
    assert.equal(m.bytes, 0); assert.equal(f.time.jobs.size, 0);
});
test("missing tracingComplete and hung IO.read are independently bounded", async t => {
    for (const hung of ["completion", "read"]) {
        const f = await fixture(t);
        await f.recorder.arm(async (...args) => {
            if (hung === "completion" && args[0] === "Tracing.end") return {};
            if (hung === "read" && args[0] === "IO.read") return new Promise(() => {});
            return f.send(...args);
        });
        const finish = f.recorder.finish(true); await flush(); f.time.tick(101); await flush(); await finish;
        assert.equal((await f.manifest()).status, "incomplete-browser-or-diagnostic-timeout");
        assert.equal(f.time.jobs.size, 0);
    }
});
test("three verifier hooks arm before navigation and preserve runtime deadline", async () => {
    for (const sample of ["Application", "Navigation", "Transitions"]) {
        const text = await readFile(new URL('../../samples/Lumyte.UI.' + sample + '.Browser.Sample/verify-browser.mjs', import.meta.url), 'utf8');
        assert.ok(text.indexOf('await flightRecorder.arm(command, browser.pid)') < text.indexOf('await command("Page.navigate"'));
        assert.match(text, /flightRecorder\.dispatch\(message/);
        assert.match(text, /flightRecorder\.request\(id/);
        assert.match(text, /method === "Runtime.evaluate" \? 45000/);
        assert.match(text, /await flightRecorder.finish\(true\)/);
    }
});

test("hung arm and clean stop are bounded without artifacts or timers", async t => {
    const f = await fixture(t);
    const arm = f.recorder.arm(() => new Promise(() => {}));
    await flush(); f.time.tick(101); await arm;
    assert.equal(f.recorder.status, "unsupported-or-start-timeout");
    await f.recorder.finish(); assert.equal(f.time.jobs.size, 0);
    assert.deepEqual(await readdir(f.outputRoot), []);
    const g = await fixture(t);
    await g.recorder.arm(async (...args) => args[0] === "Tracing.end" ? {} : g.send(...args));
    const finish = g.recorder.finish(); await flush(); g.time.tick(101); await finish;
    assert.equal(g.time.jobs.size, 0); assert.deepEqual(await readdir(g.outputRoot), []);
});
test("duration cap and duplicate concurrent finish never duplicate dump", async t => {
    const f = await fixture(t, { durationMs: 20 });
    await f.recorder.arm(f.send); f.time.tick(20); await flush();
    await Promise.all([f.recorder.finish(), f.recorder.finish()]);
    assert.equal((await f.manifest()).reason, "duration-limit");
    assert.equal(f.summaries.length, 0);
    assert.equal(f.calls.filter(x => x.method === "Tracing.end").length, 1);
    assert.equal(f.time.jobs.size, 0);
});
test("empty non-EOF reads cannot busy spin and late events cannot mutate closed capture", async t => {
    const f = await fixture(t);
    await f.recorder.arm(async (...args) => args[0] === "IO.read" ? { data: "", eof: false } : f.send(...args));
    await f.recorder.finish(true);
    const before = await f.manifest();
    f.recorder.dispatch({ method: "Tracing.tracingComplete", params: { stream: "late" } });
    f.recorder.record({ kind: "late" }); await f.recorder.finish();
    assert.equal(before.status, "incomplete-browser-or-diagnostic-timeout");
    assert.deepEqual(await f.manifest(), before);
});

test("earlier slow passing PNG never freezes the trace before a later failing PNG", async t => {
    const f = await fixture(t);
    let actualFailure = false;
    await f.recorder.arm(async (...args) => {
        if (args[0] === "IO.read") return { data: gzipSync(JSON.stringify({ traceEvents: [{ name: actualFailure ? "actual-late-failure-window" : "incorrect-early-window" }] })).toString("base64"), base64Encoded: true, eof: true };
        return f.send(...args);
    });
    let done;
    const early = f.recorder.request(2, { pngIndex: 1, stage: "early-png" }, () => new Promise(resolve => { done = resolve; }));
    f.time.tick(5); await flush(); done("png"); await early;
    assert.equal(f.recorder.status, "recording");
    assert.equal(f.calls.filter(x => x.method === "Tracing.end").length, 0);
    assert.deepEqual(await readdir(f.outputRoot), []);
    f.time.tick(40);
    actualFailure = true;
    const original = new Error("late request 19 deadline");
    await assert.rejects(f.recorder.request(19, { pngIndex: 19, stage: "late-failure-png" }, async () => { throw original; }), error => error === original);
    await f.recorder.finish(true);
    const m = await f.manifest();
    assert.equal(m.reason, "failure"); assert.equal(m.verdict, "failed");
    assert.equal(m.firstSlow.requestId, 2); assert.equal(m.firstSlow.atMs, 5);
    assert.equal(m.traceWindow.stoppedAtMs, 45); assert.equal(m.traceWindow.mode, "rolling-tail");
    assert.ok(m.records.some(x => x.kind === "png-rejected" && x.requestId === 19));
    const [directory] = await readdir(f.outputRoot);
    assert.match(gunzipSync(await readFile(join(f.outputRoot, directory, "trace.json.gz"))).toString(), /actual-late-failure-window/);
    assert.equal(f.calls.filter(x => x.method === "Tracing.end").length, 1);
    assert.equal(f.time.jobs.size, 0);
});

test("binding fallback projects known scalar diagnostics and never stores raw private payloads", async t => {
    const f = await fixture(t);
    await f.recorder.arm(f.send);
    const dispatch = payload => f.recorder.dispatch({ method: "Runtime.bindingCalled", params: { name: "__lumyteCaptureDiagnosticEvent", payload } });
    dispatch('UI Capture trace: ' + JSON.stringify({ eventId: 1, name: "capture-start", ticks: 10, frequency: 1000,
        pixels: "SECRET_PIXELS", privateText: "SECRET_TEXT", data: { requestId: 8, frameId: 7, pixels: "SECRET_NESTED", arbitrary: [1, 2, 3] } }));
    dispatch('Browser GPU trace: ' + JSON.stringify({ method: "submit", status: "start", elapsedMs: 0, id: 9, pixels: "SECRET_GPU" }));
    dispatch('UI Capture trace: malformed SECRET_BAD');
    dispatch('unknown SECRET_UNKNOWN');
    dispatch('UI Capture trace: ' + JSON.stringify({ privateText: "SECRET_INVALID" }));
    await f.recorder.finish(true);
    const m = await f.manifest();
    assert.ok(m.dropped >= 3);
    assert.ok(!JSON.stringify(m).includes("SECRET"));
    assert.ok(!JSON.stringify(m).includes('"payload"'));
    const rows = m.records.filter(x => x.kind === "binding");
    assert.equal(rows.length, 2);
    assert.deepEqual(rows[0].diagnostic.data, { requestId: 8, frameId: 7 });
    assert.equal(rows[1].diagnostic.method, "submit");
});

test("gzip artifact is readable complete JSON and ring budget differs from artifact cap", async t => {
    const f = await fixture(t);
    await f.recorder.arm(f.send); await f.recorder.finish(true);
    const m = await f.manifest();
    assert.equal(m.compression, "gzip"); assert.equal(m.traceFile, "trace.json.gz");
    assert.equal(m.incomplete, false); assert.equal(m.truncated, false);
    assert.equal(m.ringBytes, 2 * 1024 * 1024); assert.equal(m.byteLimit, 16 * 1024 * 1024);
    const [dir] = await readdir(f.outputRoot);
    const trace = JSON.parse(gunzipSync(await readFile(join(f.outputRoot, dir, m.traceFile))));
    assert.equal(trace.traceEvents.length, 1);
});

test("native recorder starts before navigation, finishes once, retains only with artifact", async t => {
    for (const failed of [false, true]) {
        const lifecycle = [];
        const f = await fixture(t, { processRecorderFactory: ({ browserPid }) => {
            assert.equal(browserPid, 234);
            return { start: async () => { lifecycle.push("start"); }, finish: async options => {
                lifecycle.push("finish"); assert.equal(options.retain, failed);
                assert.equal(typeof options.directory === "string", failed);
                return { nativeStatus: "unavailable-no-configured-tool", nativeBytes: 0 };
            } };
        } });
        await f.recorder.arm(f.send, 234); lifecycle.push("navigate");
        await f.recorder.finish(failed); await f.recorder.finish(failed);
        assert.deepEqual(lifecycle, ["start", "navigate", "finish"]);
        if (failed) assert.equal((await f.manifest()).processRecorder.nativeStatus, "unavailable-no-configured-tool");
    }
});
test("native recorder errors cannot mask original verdict", async t => {
    const f = await fixture(t, { processRecorderFactory: () => ({ start: async () => { throw Error("proc denied"); },
        finish: async () => { throw Error("perf denied"); } }) });
    await f.recorder.arm(f.send, 234);
    const original = new Error("pixel failure");
    await assert.rejects(f.recorder.request(7, {}, async () => { throw original; }), x => x === original);
    await f.recorder.finish(true);
    assert.equal((await f.manifest()).processRecorder.status, "finish-unavailable");
});

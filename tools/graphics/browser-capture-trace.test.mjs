import assert from "node:assert/strict";
import test from "node:test";
import vm from "node:vm";
import { readFile, readdir } from "node:fs/promises";
import { captureDiagnosticsEnabled, installCaptureDiagnosticsRequest, createCaptureTraceReporter, captureTracePrefix } from "./browser-capture-trace.mjs";

const row = (id = 7) => captureTracePrefix + JSON.stringify({ eventId: 12, name: "CaptureState", ticks: 12, frequency: 1000, data: { captureId: id, frameId: 9, profileRunId: "run", phase: 3 } });

test("capture diagnostics are opt-in locally and automatic under the existing CI software gate", () => {
    assert.equal(captureDiagnosticsEnabled({}), false);
    assert.equal(captureDiagnosticsEnabled({ LUMYTE_BROWSER_CAPTURE_TRACE: "0" }), false);
    assert.equal(captureDiagnosticsEnabled({ LUMYTE_BROWSER_CAPTURE_TRACE: "1" }), true);
    assert.equal(captureDiagnosticsEnabled({ LUMYTE_REQUIRE_SOFTWARE_WEBGPU: "1" }), true);
});

test("serialized pre-start hook only sets the enable flag, without rendering, GPU calls or timers", () => {
    const target = {};
    vm.runInNewContext(`(${installCaptureDiagnosticsRequest.toString()})();`, target);
    assert.deepEqual(Object.keys(target), ["__lumyteCaptureDiagnosticsRequested"]);
    assert.equal(target.__lumyteCaptureDiagnosticsRequested, true);
});

test("structured request/frame identity is preserved and unrelated output is ignored", () => {
    const output = [];
    const reporter = createCaptureTraceReporter({ emit: x => output.push(x) });
    assert.equal(reporter.observe("ordinary application output"), false);
    assert.equal(reporter.observe(row(), { sample: "transitions", stage: "entry-midpoint-png" }), true);
    assert.equal(reporter.received, 1);
    assert.equal(output[0].event.data.captureId, 7);
    assert.equal(output[0].event.data.frameId, 9);
    assert.equal(output[0].event.data.profileRunId, "run");
    assert.equal(output[0].stage, "entry-midpoint-png");
});

test("invalid, nested and oversized rows are bounded without forwarding raw data", () => {
    const output = [];
    const reporter = createCaptureTraceReporter({ emit: x => output.push(x) });
    for (const invalid of ["not-json", JSON.stringify({ eventId: 1 }), "x".repeat(17000), JSON.stringify({ eventId: 12, name: "CaptureState", ticks: 0, frequency: 1, data: { image: { pixels: [1] } } })]) {
        assert.equal(reporter.observe(captureTracePrefix + invalid), true);
    }
    assert.equal(reporter.invalid, 4);
    assert.deepEqual(output, [{ check: "browser-capture-diagnostics", status: "invalid-row" }]);
});

test("failure snapshots and final history survive streaming truncation", async () => {
    const output = [];
    const reporter = createCaptureTraceReporter({ emit: x => output.push(x), maxRecords: 2, historyLimit: 2 });
    for (let i = 1; i <= 5; i++) reporter.observe(row(i));
    assert.equal(reporter.dropped, 3);
    assert.equal(output.filter(x => x.status === "trace-truncated").length, 1);
    const commands = [];
    await reporter.failure(async (...args) => {
        commands.push(args);
        return { result: { value: JSON.stringify({ active: [{ captureId: 5, phase: "gpu-wait" }], truncated: true }) } };
    }, { stage: "entry-midpoint-png" });
    assert.equal(commands.length, 1);
    assert.equal(commands[0][0], "Runtime.evaluate");
    assert.equal(commands[0][1].awaitPromise, false);
    assert.equal(commands[0][2], 2000);
    const failure = output.at(-1);
    assert.equal(failure.status, "available");
    assert.equal(failure.snapshot.active[0].captureId, 5);
    assert.deepEqual(failure.recent.map(x => x.data.captureId), [4, 5]);
    assert.equal(failure.dropped, 3);
});

test("query/export/serialization failures and broken emitters do not replace the original failure", async () => {
    const original = new Error("original managed deadline");
    const output = [];
    const reporter = createCaptureTraceReporter({ emit: x => output.push(x) });
    await assert.rejects(async () => {
        try { throw original; } catch (failure) {
            await reporter.failure(async () => { throw new Error("diagnostics unavailable"); });
            throw failure;
        }
    }, failure => failure === original);
    assert.equal(output.at(-1).status, "snapshot-query-failed");
    await reporter.failure(async () => ({ exceptionDetails: {}, result: {} }));
    assert.equal(output.at(-1).status, "export-error");
    await reporter.failure(async () => ({ result: { value: "bad-json" } }));
    assert.equal(output.at(-1).status, "snapshot-query-failed");
    await reporter.failure(async () => ({ result: { value: "x".repeat(131073) } }));
    assert.equal(output.at(-1).status, "snapshot-too-large");
    const broken = createCaptureTraceReporter({ emit: () => { throw new Error("log sink"); } });
    assert.doesNotThrow(() => broken.observe(row()));
    await broken.failure(undefined);
});

test("all public Browser verifiers enable before navigation and dump before cleanup, retaining original deadlines and verdicts", async () => {
    for (const name of ["Application", "Navigation", "Transitions"]) {
        const base = new URL(`../../samples/Lumyte.UI.${name}.Browser.Sample/`, import.meta.url);
        const verifier = await readFile(new URL("verify-browser.mjs", base), "utf8");
        const main = await readFile(new URL("wwwroot/main.js", base), "utf8");
        assert.ok(verifier.indexOf("installCaptureDiagnosticsRequest.toString()") < verifier.indexOf("await command(\"Page.navigate\""));
        assert.ok(main.indexOf("EnableCaptureDiagnostics()") < main.indexOf("await runtime.runMain"));
        assert.match(verifier, /timeoutMs \?\? \(method === "Page.navigate" \? 60000 : method === "Runtime.evaluate" \? 45000 : 10000\)/);
        assert.ok(verifier.indexOf("await captureTrace.failure(") < verifier.indexOf("socket?.close()"));
        if (name === "Transitions") {
            assert.ok(verifier.indexOf("await captureTrace.failure(") < verifier.indexOf("if (!shouldIgnoreKnownBrowserTimeout("));
            assert.match(verifier, /throw failure;/);
        } else assert.match(verifier, /catch \(failure\) \{[\s\S]*await captureTrace.failure[\s\S]*throw failure;/);
    }
});

test("undeclared top-level data is never forwarded or retained", async () => {
    const output = [];
    const reporter = createCaptureTraceReporter({ emit: x => output.push(x) });
    const event = JSON.parse(row().slice(captureTracePrefix.length));
    event.pixels = [255, 0, 0, 255];
    event.input = { text: "private input" };
    reporter.observe(captureTracePrefix + JSON.stringify(event));
    await reporter.failure(undefined);
    assert.deepEqual(Object.keys(output[0].event).sort(), ["data", "eventId", "frequency", "name", "ticks"]);
    assert.equal(JSON.stringify(output).includes("private input"), false);
});

test("recognized diagnostic rows do not fall through to unbounded verifier progress", async () => {
    for (const name of ["Application", "Navigation", "Transitions"]) {
        const verifier = await readFile(new URL(`../../samples/Lumyte.UI.${name}.Browser.Sample/verify-browser.mjs`, import.meta.url), "utf8");
        assert.match(verifier, /const captureObserved = traceCapture && captureTrace.observe/);
        assert.match(verifier, /else if \(captureObserved\)/);
    }
});

test("every Browser project compiling a diagnostic probe includes its managed observer and EventSource support", async () => {
    const samples = new URL("../../samples/", import.meta.url);
    const projects = (await readdir(samples, { withFileTypes: true })).filter(entry => entry.isDirectory() && entry.name.endsWith(".Browser.Sample"));
    let checked = 0;
    for (const entry of projects) {
        const directory = new URL(entry.name + "/", samples);
        const project = await readFile(new URL(entry.name + ".csproj", directory), "utf8");
        const probes = [...project.matchAll(/<Compile\b[^>]*\bInclude="([^"]*BrowserProbe\.cs)"/g)].map(match => new URL(match[1], directory));
        probes.push(new URL("BrowserProbe.cs", directory));
        let usesDiagnostics = false;
        for (const probe of probes) {
            try { usesDiagnostics ||= (await readFile(probe, "utf8")).includes("Lumyte.UI.SampleSupport.UiCaptureDiagnostics"); }
            catch (error) { if (error.code !== "ENOENT") throw error; }
        }
        if (!usesDiagnostics) continue;
        checked++;
        const startup = await readFile(new URL("wwwroot/main.js", directory), "utf8");
        assert.ok(startup.indexOf("EnableCaptureDiagnostics()") >= 0 && startup.indexOf("EnableCaptureDiagnostics()") < startup.indexOf("await runtime.runMain"), entry.name + " must enable requested diagnostics before runMain");
        assert.match(project, /<Compile\b[^>]*\bInclude="[^"]*UiCaptureDiagnostics\.cs"/, entry.name + " must compile the observer required by its own or linked probe");
        assert.match(project, /<EventSourceSupport>\s*true\s*<\/EventSourceSupport>/, entry.name + " must retain EventSource support for trimmed diagnostics");
    }
    assert.ok(checked >= 4, "Cover Application, Navigation, Pages and Transitions, including source-linked consumers");
});

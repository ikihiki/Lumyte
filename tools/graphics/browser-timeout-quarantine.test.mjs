import assert from "node:assert/strict";
import test from "node:test";
import { BrowserEvaluationFailure, shouldIgnoreKnownBrowserTimeout } from "./browser-known-timeout.mjs";
import { classifyBrowserTimeoutQuarantine as classify } from "./browser-timeout-quarantine.mjs";
// Sanitized minimal replay of actual m3 failed sequential report (#172).
// Original timings, spans, frames and dropped-history counts; UUIDs replaced. No payloads.
const replay = {
    "check": "browser-capture-failure",
    "sample": "transitions",
    "stage": "remaining-transitions",
    "pngIndex": 19,
    "status": "available",
    "received": 2135,
    "invalid": 0,
    "dropped": 1623,
    "records": [
        {
            "sample": "transitions",
            "stage": "connected-first-png",
            "pngIndex": 19,
            "event": {
                "eventId": 1,
                "name": "StageBegin",
                "ticks": 108968500000,
                "frequency": 1000000000,
                "data": {
                    "profileRunId": "11111111-1111-4111-8111-111111111111",
                    "frameId": 69,
                    "spanId": 22887,
                    "parentSpanId": 22873,
                    "stage": 14
                }
            }
        },
        {
            "sample": "transitions",
            "stage": "connected-first-png",
            "pngIndex": 19,
            "event": {
                "eventId": 12,
                "name": "CaptureState",
                "ticks": 117859599999,
                "frequency": 1000000000,
                "data": {
                    "requestId": 19,
                    "profileRunId": "00000000-0000-0000-0000-000000000000",
                    "frameId": 0,
                    "sourceId": "00000000-0000-0000-0000-000000000000",
                    "phase": 9,
                    "reason": 1,
                    "processingCount": 0,
                    "owned": 0,
                    "drawingBusy": 0
                }
            }
        }
    ],
    "gpu": [
        {
            "sample": "transitions",
            "stage": "connected-first-png",
            "pngIndex": 19,
            "event": {
                "method": "submit",
                "status": "start",
                "elapsedMs": 0,
                "id": 337,
                "atMs": 108967.8999999999
            }
        },
        {
            "sample": "transitions",
            "stage": "connected-first-png",
            "pngIndex": 19,
            "event": {
                "method": "submit",
                "status": "returned",
                "elapsedMs": 0,
                "id": 337,
                "atMs": 108967.8999999999
            }
        },
        {
            "sample": "transitions",
            "stage": "connected-first-png",
            "pngIndex": 19,
            "event": {
                "method": "onSubmittedWorkDone",
                "status": "start",
                "elapsedMs": 0,
                "id": 338,
                "atMs": 108968
            }
        }
    ],
    "captures": [
        {
            "index": 19,
            "stage": "connected-first-png",
            "status": "start",
            "elapsedMs": 0.001472999996622093
        },
        {
            "index": 19,
            "stage": "connected-first-png",
            "status": "rejected",
            "elapsedMs": 30056.107206999994
        }
    ],
    "snapshot": {
        "enabled": true,
        "dropped": 1409,
        "streamed": 1459,
        "events": [
            {
                "eventId": 12,
                "name": "CaptureState",
                "ticks": 108833500000,
                "frequency": 1000000000,
                "data": {
                    "requestId": 19,
                    "profileRunId": "11111111-1111-4111-8111-111111111111",
                    "frameId": 69,
                    "sourceId": "22222222-2222-4222-8222-222222222222",
                    "phase": 4,
                    "reason": 0,
                    "processingCount": 1,
                    "owned": 1,
                    "drawingBusy": 0
                }
            }
        ],
        "requests": [
            {
                "eventId": 12,
                "name": "CaptureState",
                "ticks": 117859599999,
                "frequency": 1000000000,
                "data": {
                    "requestId": 19,
                    "profileRunId": "00000000-0000-0000-0000-000000000000",
                    "frameId": 0,
                    "sourceId": "00000000-0000-0000-0000-000000000000",
                    "phase": 9,
                    "reason": 1,
                    "processingCount": 0,
                    "owned": 0,
                    "drawingBusy": 0
                }
            }
        ],
        "terminalFrames": [
            {
                "requestId": 19,
                "profileRunId": "11111111-1111-4111-8111-111111111111",
                "frameId": 69,
                "observation": "atTerminal",
                "openStages": [
                    {
                        "eventId": 1,
                        "name": "StageBegin",
                        "ticks": 108833299999,
                        "frequency": 1000000000,
                        "data": {
                            "profileRunId": "11111111-1111-4111-8111-111111111111",
                            "frameId": 69,
                            "spanId": 22869,
                            "parentSpanId": 0,
                            "stage": 6
                        }
                    },
                    {
                        "eventId": 1,
                        "name": "StageBegin",
                        "ticks": 108834000000,
                        "frequency": 1000000000,
                        "data": {
                            "profileRunId": "11111111-1111-4111-8111-111111111111",
                            "frameId": 69,
                            "spanId": 22871,
                            "parentSpanId": 22869,
                            "stage": 20
                        }
                    },
                    {
                        "eventId": 1,
                        "name": "StageBegin",
                        "ticks": 108834399999,
                        "frequency": 1000000000,
                        "data": {
                            "profileRunId": "11111111-1111-4111-8111-111111111111",
                            "frameId": 69,
                            "spanId": 22873,
                            "parentSpanId": 22869,
                            "stage": 8
                        }
                    },
                    {
                        "eventId": 1,
                        "name": "StageBegin",
                        "ticks": 108968500000,
                        "frequency": 1000000000,
                        "data": {
                            "profileRunId": "11111111-1111-4111-8111-111111111111",
                            "frameId": 69,
                            "spanId": 22887,
                            "parentSpanId": 22873,
                            "stage": 14
                        }
                    }
                ],
                "lastStage": {
                    "eventId": 1,
                    "name": "StageBegin",
                    "ticks": 108968500000,
                    "frequency": 1000000000,
                    "data": {
                        "profileRunId": "11111111-1111-4111-8111-111111111111",
                        "frameId": 69,
                        "spanId": 22887,
                        "parentSpanId": 22873,
                        "stage": 14
                    }
                }
            }
        ]
    }
};
function candidate() {
    return {
        environment: { LUMYTE_QUARANTINE_CONNECTED_PNG_TIMEOUT: "1", LUMYTE_REQUIRE_SOFTWARE_WEBGPU: "1" },
        platform: "linux", sequential: true, sameContent: false,
        stage: "remaining-transitions", captureStage: "connected-first-png", pngIndex: 19,
        failure: new BrowserEvaluationFailure("deadline", {
            expression: "globalThis.lumyteApplicationProbe.CapturePngAsync()", state: "Running;Animating;",
            details: { exception: { className: "ManagedError", description: "ManagedError: The automation deadline expired." } },
        }), errors: [], audit: { available: true, requests: [{ adapter: {
            vendor: "google", architecture: "swiftshader", isFallbackAdapter: true,
        } }] }, evidence: structuredClone(replay), now: Date.parse("2026-11-17T08:00:00.000Z"),
    };
}
test("saved report is inconclusive, not success; no raw errors returned", () => {
    const value = classify(candidate());
    assert.equal(value.status, "inconclusive-known-timeout");
    assert.equal(value.expiry, "2026-12-01T00:00:00.000Z");
    assert.equal(value.issue, "https://github.com/ikihiki/Lumyte/issues/172");
    assert.equal(JSON.stringify(value).includes("ManagedError"), false);
});
const negatives = {
    "pixel assertion": x => { x.failure = new assert.AssertionError({ message: "pixel mismatch" }); },
    "lookalike": x => { x.failure = { ...x.failure }; },
    "exception type": x => { x.failure.details.exception.className = "Error"; },
    "exception description": x => { x.failure.details.exception.description += " extra"; },
    "expression": x => { x.failure.expression = "CapturePngAsync()"; },
    "state": x => { x.failure.state = "Running;"; },
    "nonsequential": x => { x.sequential = false; },
    "same content": x => { x.sameContent = true; },
    "stage": x => { x.stage = "entry-midpoint-png"; },
    "capture stage": x => { x.captureStage = "readback"; },
    "PNG": x => { x.pngIndex = 18; },
    "platform": x => { x.platform = "win32"; },
    "missing flag": x => { delete x.environment.LUMYTE_QUARANTINE_CONNECTED_PNG_TIMEOUT; },
    "inexact flag": x => { x.environment.LUMYTE_QUARANTINE_CONNECTED_PNG_TIMEOUT = "true"; },
    "software flag": x => { delete x.environment.LUMYTE_REQUIRE_SOFTWARE_WEBGPU; },
    "hardware": x => { x.audit.requests[0].adapter.architecture = "hardware"; },
    "missing adapter": x => { x.audit.requests = []; },
    "other error": x => { x.errors.push("CDP failure"); },
    "missing errors": x => { delete x.errors; },
    "device loss": x => { x.evidence.gpu.unshift({ event: { method: "deviceLost", status: "fulfilled" } }); },
    "missing evidence": x => { delete x.evidence; },
    "missing snapshot": x => { delete x.evidence.snapshot; },
    "invalid report": x => { x.evidence.invalid = 1; },
    "truncated": x => { x.evidence.truncated = true; },
    "wrong request": x => { x.evidence.snapshot.requests[0].data.requestId = 18; },
    "terminal phase": x => { x.evidence.snapshot.requests[0].data.phase = 8; },
    "terminal reason": x => { x.evidence.snapshot.requests[0].data.reason = 2; },
    "terminal frame": x => { x.evidence.snapshot.terminalFrames[0].frameId = 0; },
    "observation": x => { x.evidence.snapshot.terminalFrames[0].observation = "later"; },
    "missing frame link": x => { x.evidence.snapshot.events = []; },
    "readback stage": x => { x.evidence.snapshot.terminalFrames[0].openStages.at(-1).data.stage = 15; },
    "GPU context": x => { x.evidence.gpu.at(-1).stage = "prepare"; },
    "GPU id": x => { delete x.evidence.gpu.at(-1).event.id; },
    "fulfilled anywhere": x => { x.evidence.gpu.unshift({ ...x.evidence.gpu.at(-1), event: { ...x.evidence.gpu.at(-1).event, status: "fulfilled" } }); },
    "short capture": x => { x.evidence.captures.at(-1).elapsedMs = 29999; },
    "long capture": x => { x.evidence.captures.at(-1).elapsedMs = 35001; },
    "expiry boundary": x => { x.now = Date.parse("2026-12-01T00:00:00.000Z"); },
    "after expiry": x => { x.now = Date.parse("2026-12-02T00:00:00.000Z"); },
    "invalid clock": x => { x.now = NaN; },
    "string clock": x => { x.now = "2026-11-17"; },
    "implausible clock": x => { x.now = 0; },
    "ended stage": x => {
        const end = structuredClone(x.evidence.snapshot.terminalFrames[0].openStages.at(-1));
        end.name = "StageEnd"; end.eventId = 2; end.data.outcome = 0;
        x.evidence.snapshot.events.push(end);
    },
};
for (const [name, mutate] of Object.entries(negatives)) test(name + " fails closed", () => {
    const input = candidate(); mutate(input); assert.equal(classify(input), undefined);
});
test("CPU process report not required; host vendor not assumed", () => {
    const input = candidate(); input.environment.HOST_GPU_VENDOR = "not AMD";
    assert.equal(classify(input).status, "inconclusive-known-timeout");
});

async function runProductionCatch(value, { artifact = true, nativeDiagnostics } = {}) {
    const { readFile } = await import("node:fs/promises");
    const source = await readFile(new URL("../../samples/Lumyte.UI.Transitions.Browser.Sample/verify-browser.mjs", import.meta.url), "utf8");
    const start = source.lastIndexOf("} catch (failure) {") + "} catch (failure) {".length;
    const body = source.slice(start, source.indexOf("} finally {", start));
    const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
    const keys = ["failure", "nativeRecorder", "nativeEnabled", "nativeReport", "flightRecorder", "traceCapture", "traceGpu", "captureTrace", "command", "sequential", "sameContent", "stage", "pngIndex", "traceStage", "errors", "process", "console", "BrowserEvaluationFailure", "shouldIgnoreKnownBrowserTimeout", "classifyBrowserTimeoutQuarantine"];
    const output = []; let finalized = false;
    const recorder = { finish: async () => { finalized = true; }, markQuarantined: async (policy, eligible) => {
        assert.equal(finalized, true); return artifact && eligible() ? { ...policy, occurrences: 1 } : undefined;
    } };
    const args = [value.failure, nativeDiagnostics?.recorder, nativeDiagnostics?.enabled ?? false,
        nativeDiagnostics?.report ?? (() => assert.fail("Disabled native reporter must not run")),
        recorder, true, true, { failure: async () => value.evidence },
        async () => ({ result: { value: value.audit } }), value.sequential, value.sameContent, value.stage, value.pngIndex,
        value.captureStage, value.errors, { env: value.environment },
        { error: () => {}, warn: text => output.push(text), log: text => output.push(JSON.parse(text)) },
        BrowserEvaluationFailure, shouldIgnoreKnownBrowserTimeout, args => classify({ ...args, platform: value.platform, now: value.now })];
    await new AsyncFunction(...keys, body)(...args);
    return output;
}
test("production catch replays saved signature as warning/inconclusive, never scenario passed", async () => {
    const output = await runProductionCatch(candidate());
    assert.match(output[0], /::warning/);
    assert.equal(output[1].status, "inconclusive-known-timeout");
    assert.equal(output[1].remainingAssertionsExecuted, false);
    assert.equal(output[1].occurrences, 1);
});
for (const kind of ["pixel", "device-loss", "missing-artifact", "expiry", "missing-diagnostics"]) {
    test("production catch preserves fatal original error: " + kind, async () => {
        const c = candidate();
        if (kind === "pixel") c.failure = new assert.AssertionError({ message: "pixel mismatch" });
        if (kind === "device-loss") c.errors.push("GPU device lost");
        if (kind === "expiry") c.now = Date.parse("2026-12-01T00:00:00.000Z");
        if (kind === "missing-diagnostics") c.evidence = undefined;
        await assert.rejects(runProductionCatch(c, { artifact: kind !== "missing-artifact" }), error => error === c.failure);
    });
}

test("production catch preserves quarantine with native diagnostics enabled", async () => {
    const events = [];
    const snapshot = { owned: true };
    const output = await runProductionCatch(candidate(), { nativeDiagnostics: {
        enabled: true,
        recorder: { event: name => events.push(name), snapshot: () => snapshot },
        report: (phase, value) => { assert.equal(value, snapshot); events.push(phase); },
    } });
    assert.deepEqual(events, ["failure-observed", "before-cleanup-failure"]);
    assert.match(output[0], /::warning/);
    assert.equal(output[1].status, "inconclusive-known-timeout");
    assert.equal(output[1].remainingAssertionsExecuted, false);
});
test("production catch preserves original fatal error with native diagnostics enabled", async () => {
    const c = candidate();
    c.failure = new assert.AssertionError({ message: "pixel mismatch" });
    const events = [];
    const snapshot = { owned: true };
    await assert.rejects(runProductionCatch(c, { nativeDiagnostics: {
        enabled: true,
        recorder: { event: name => events.push(name), snapshot: () => snapshot },
        report: (phase, value) => { assert.equal(value, snapshot); events.push(phase); },
    } }), error => error === c.failure);
    assert.deepEqual(events, ["failure-observed", "before-cleanup-failure"]);
});
test("production catch does not report disabled native diagnostics", async () => {
    let reports = 0;
    await runProductionCatch(candidate(), { nativeDiagnostics: { enabled: false, report: () => reports++ } });
    assert.equal(reports, 0);
});
test("production catch tolerates an absent optional native recorder", async () => {
    const reports = [];
    const output = await runProductionCatch(candidate(), { nativeDiagnostics: {
        enabled: true, report: (phase, value) => reports.push([phase, value]),
    } });
    assert.deepEqual(reports, [["before-cleanup-failure", undefined]]);
    assert.equal(output[1].status, "inconclusive-known-timeout");
});

for (const path of ["requests", "terminalFrames", "openStages"]) {
    for (const malformed of [null, {}, { data: {} }]) {
        test("malformed snapshot entry fails closed: " + path + JSON.stringify(malformed), () => {
            const c = candidate();
            const list = path === "openStages" ? c.evidence.snapshot.terminalFrames.at(-1).openStages : c.evidence.snapshot[path];
            list.push(malformed);
            assert.equal(classify(c), undefined);
        });
    }
}

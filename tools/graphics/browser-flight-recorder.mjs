import { createBrowserProcessRecorder } from "./browser-process-recorder.mjs";
import { mkdir, mkdtemp, readFile, writeFile } from "node:fs/promises";
import { resolve, join } from "node:path";

// Chrome owns the rolling trace buffer. No screenshots, DOM snapshots, network
// payloads, user timing or console text categories. These are scheduled CPU/GPU
// events, NOT a guaranteed native SwiftShader CPU stack or hardware GPU profile.
export const flightRecorderCategories = ["toplevel", "gpu", "disabled-by-default-gpu.service", "disabled-by-default-toplevel.flow"];
export function browserFlightRecorderEnabled(environment = process.env) {
    return environment.LUMYTE_REQUIRE_SOFTWARE_WEBGPU === "1" || environment.LUMYTE_BROWSER_FLIGHT_RECORDER === "1";
}
const bounded = (value, fallback, min, max) => Number.isFinite(Number(value)) && Number(value) >= min && Number(value) <= max ? Number(value) : fallback;
export function createBrowserFlightRecorder({ sample, mode = "default", environment = process.env,
    enabled = browserFlightRecorderEnabled(environment), outputRoot = resolve("artifacts/test-results/browser-flight-recorder"),
    emit = value => console.log(JSON.stringify(value)), now = () => performance.now(),
    schedule = setTimeout, cancel = clearTimeout,
    slowMs = bounded(environment.LUMYTE_BROWSER_FLIGHT_RECORDER_SLOW_MS, 5000, 1, 60000),
    byteLimit = bounded(environment.LUMYTE_BROWSER_FLIGHT_RECORDER_BYTES, 16 * 1024 * 1024, 1024, 64 * 1024 * 1024),
    ringBytes = bounded(environment.LUMYTE_BROWSER_FLIGHT_RECORDER_RING_BYTES, 2 * 1024 * 1024, 65536, 8 * 1024 * 1024),
    budgetMs = bounded(environment.LUMYTE_BROWSER_FLIGHT_RECORDER_BUDGET_MS, 4000, 10, 10000),
    durationMs = 300000, historyBytes = 256 * 1024, processRecorderFactory = createBrowserProcessRecorder } = {}) {
    let command, armed = false, armPromise, dumpPromise, complete, acceptComplete = false, closed = false, durationTimer, finishPromise, processRecorder, processMetadata;
    let status = enabled ? "not-armed" : "disabled", reason, directory, bytes = 0, truncated = false;
    let received = 0, dropped = 0, historySize = 0, sequence = 0, pngIndex = 0, active, firstSlow, slowCount = 0, traceStoppedAtMs;
    const history = [], timers = new Set();
    const start = now();
    function record(value) {
        if (!enabled || closed) return;
        const row = { sequence: ++sequence, atMs: now() - start, ...value };
        const size = Buffer.byteLength(JSON.stringify(row));
        received++;
        if (size > historyBytes) { dropped++; return; }
        history.push({ row, size }); historySize += size;
        while (historySize > historyBytes) { historySize -= history.shift().size; dropped++; }
    }
    function timer(action, ms) {
        const id = schedule(() => { timers.delete(id); action(); }, ms);
        timers.add(id); return id;
    }
    function clear(id) { if (id !== undefined) { cancel(id); timers.delete(id); } }
    async function boundedCall(method, params, deadline) {
        const remaining = deadline - now();
        if (remaining <= 0) throw new Error("diagnostic-budget");
        let id;
        try {
            return await Promise.race([
                Promise.resolve().then(() => command(method, params, Math.max(1, Math.ceil(remaining)))),
                new Promise((_, reject) => { id = timer(() => reject(new Error("diagnostic-timeout")), remaining); }),
            ]);
        } finally { clear(id); }
    }
    function projectDiagnostic(payload) {
        if (typeof payload !== "string" || payload.length > 16384) return;
        try {
            const ui = payload.startsWith("UI Capture trace: ");
            const gpu = payload.startsWith("Browser GPU trace: ");
            if (!ui && !gpu) return;
            const event = JSON.parse(payload.slice(ui ? 18 : 19));
            if (!event || typeof event !== "object" || Array.isArray(event)) return;
            if (ui) {
                if (!Number.isInteger(event.eventId) || typeof event.name !== "string" || event.name.length > 128
                    || !Number.isFinite(event.ticks) || !Number.isFinite(event.frequency) || event.frequency <= 0
                    || !event.data || typeof event.data !== "object" || Array.isArray(event.data)
                    || Object.keys(event.data).length > 48) return;
                // Deliberately narrower than the existing reporter: only known
                // correlation/counter keys; never arbitrary strings or payloads.
                const data = {};
                const known = /^(requestId|frameId|frameIndex|parentFrameId|pngIndex|stage|contextId|method|status|elapsedMs|width|height|count|bytes|pending|submitted|completed|generation|sequence)$/;
                for (const [key, value] of Object.entries(event.data)) {
                    if (!known.test(key)) continue;
                    if (value === null || typeof value === "boolean" || (typeof value === "number" && Number.isFinite(value))
                        || (typeof value === "string" && value.length <= 128)) data[key] = value;
                    else return;
                }
                return { source: "capture", eventId: event.eventId, name: event.name, ticks: event.ticks, frequency: event.frequency, data };
            }
            if (typeof event.method !== "string" || event.method.length > 80 || typeof event.status !== "string"
                || event.status.length > 32 || !Number.isFinite(event.elapsedMs)) return;
            return { source: "gpu", method: event.method, status: event.status, elapsedMs: event.elapsedMs,
                ...(Number.isSafeInteger(event.id) ? { id: event.id } : {}),
                ...(Number.isFinite(event.atMs) ? { atMs: event.atMs } : {}),
                ...(event.truncated === true ? { truncated: true } : {}) };
        } catch { return; }
    }
    function dispatch(message, context = {}) {
        if (!enabled || closed) return;
        if (message.method === "Tracing.tracingComplete") {
            if (acceptComplete && complete) { const resolve = complete; complete = undefined; resolve(message.params ?? {}); }
            return;
        }
        // Only the existing bounded native diagnostic binding, never general
        // console/exception text, is retained as the hung-browser fallback.
        if (message.method === "Runtime.bindingCalled" && message.params?.name === "__lumyteCaptureDiagnosticEvent") {
            const payload = message.params.payload;
            const diagnostic = projectDiagnostic(payload);
            if (diagnostic) record({ kind: "binding", executionContextId: message.params.executionContextId, ...context, ...(active ?? {}), diagnostic });
            else dropped++;
        }
    }
    function arm(send, browserPid) {
        if (armPromise) return armPromise;
        if (!enabled || closed) return Promise.resolve();
        command = send;
        armPromise = (async () => {
            if (Number.isSafeInteger(browserPid) && browserPid > 0) {
                try {
                    processRecorder = processRecorderFactory({ browserPid, environment });
                    await processRecorder.start();
                    record({ kind: "process-recorder-started", browserPid });
                } catch { record({ kind: "process-recorder-unavailable", browserPid }); }
            }
            try {
                await boundedCall("Tracing.start", { transferMode: "ReturnAsStream", streamFormat: "json", streamCompression: "gzip", traceConfig: {
                    recordMode: "recordContinuously", traceBufferSizeInKb: Math.ceil(ringBytes / 1024),
                    includedCategories: flightRecorderCategories, excludedCategories: ["*"],
                    enableArgumentFilter: true,
                } }, now() + budgetMs);
                armed = true; status = "recording"; record({ kind: "armed" });
                durationTimer = timer(() => { void dump("duration-limit"); }, durationMs);
            } catch { status = "unsupported-or-start-timeout"; record({ kind: "arm-unavailable" }); }
        })();
        return armPromise;
    }
    function manifest() {
        return { version: 1, sample, mode, reason, status, bytes, byteLimit, ringBytes, compression: "gzip", traceFile: bytes ? "trace.json.gz" : undefined,
            incomplete: truncated || status !== "saved", truncated, slowMs, processRecorder: processMetadata,
            received, dropped, historyBytes: historySize, categories: flightRecorderCategories,
            slowCount, firstSlow, traceWindow: { mode: "rolling-tail", stoppedAtMs: traceStoppedAtMs, durationLimitMs: durationMs,
                note: "Chrome retains only the bounded rolling tail ending at verifier finish/failure (or duration cap); earlier slow requests may have rolled out." },
            limitations: "Chrome scheduled CPU/GPU/flow trace only; native SwiftShader stacks and hardware GPU timing are not guaranteed. A truncated or incomplete compressed stream is not a valid complete trace. Hung browser falls back to received metadata.",
            records: history.map(value => value.row) };
    }
    function dump(trigger) {
        if (!enabled || closed) return Promise.resolve();
        if (dumpPromise) return dumpPromise;
        reason = trigger;
        traceStoppedAtMs = now() - start;
        dumpPromise = (async () => {
            clear(durationTimer);
            let handle, reachedEof = false;
            const chunks = [];
            const deadline = now() + budgetMs;
            try {
                if (armed) {
                    acceptComplete = true;
                    const completion = new Promise(resolve => { complete = resolve; });
                    await boundedCall("Tracing.end", {}, deadline);
                    let id;
                    let result;
                    try {
                        result = await Promise.race([completion, new Promise((_, reject) => {
                            id = timer(() => reject(new Error("tracing-complete-timeout")), Math.max(1, deadline - now()));
                        })]);
                    } finally { clear(id); }
                    handle = result.stream;
                    if (!handle) throw new Error("no-stream");
                    status = "saved";
                    while (bytes < byteLimit) {
                        const part = await boundedCall("IO.read", { handle, size: Math.min(65536, byteLimit - bytes) }, deadline);
                        // Reject a non-compliant oversized response before decoding it.
                        if (typeof part.data !== "string" || part.data.length > 4 * 65536) throw new Error("oversized-read");
                        const buffer = Buffer.from(part.data, part.base64Encoded ? "base64" : "utf8");
                        const count = Math.min(buffer.length, byteLimit - bytes);
                        chunks.push(buffer.subarray(0, count)); bytes += count;
                        if (count < buffer.length || (!part.eof && bytes === byteLimit)) { truncated = true; status = "size-limit"; break; }
                        if (part.eof) { reachedEof = true; break; }
                        if (buffer.length === 0) throw new Error("empty-stream-read");
                    }
                } else status = "trace-unavailable";
            } catch { status = "incomplete-browser-or-diagnostic-timeout"; }
            finally {
                acceptComplete = false; complete = undefined; armed = false;
                if (bytes > 0 && !reachedEof) truncated = true;
                if (handle) {
                    try { await boundedCall("IO.close", { handle }, now() + Math.min(250, budgetMs)); } catch { /* browser teardown also releases stream */ }
                }
            }
            try {
                await mkdir(outputRoot, { recursive: true });
                const safe = value => String(value).replace(/[^a-zA-Z0-9_-]/g, "-").slice(0, 80);
                directory = await mkdtemp(join(outputRoot, safe(sample) + "-" + safe(mode) + "-"));
                if (bytes) await writeFile(join(directory, "trace.json.gz"), Buffer.concat(chunks, bytes));
                await writeFile(join(directory, "manifest.json"), JSON.stringify(manifest(), null, 2) + "\n");
            } catch { status = "artifact-write-failed"; }
        })();
        return dumpPromise;
    }
    async function request(requestId, context, action) {
        if (!enabled || closed) return action();
        const index = context?.pngIndex ?? ++pngIndex;
        active = { requestId, pngIndex: index, ...context };
        const metadata = active;
        record({ kind: "png-start", ...metadata });
        const id = timer(() => {
            slowCount++;
            firstSlow ??= { atMs: now() - start, ...metadata };
            record({ kind: "png-slow", ...metadata });
            // Do not freeze an early slow-but-passing PNG: later failures must
            // still have their own pre-failure rolling Chrome history.
        }, slowMs);
        try { const result = await action(); record({ kind: "png-fulfilled", ...metadata }); return result; }
        catch (error) { record({ kind: "png-rejected", ...metadata }); throw error; }
        finally { clear(id); active = undefined; }
    }
    async function finalize(failed = false) {
        if (!enabled || closed) return;
        record({ kind: failed ? "verifier-failed" : "verifier-passed" });
        if (failed) await dump("failure");
        else if (dumpPromise) await dumpPromise;
        else if (slowCount > 0) await dump("slow-png");
        else if (armed) {
            // Stop clean recording, close its stream, but never create an artifact.
            const deadline = now() + budgetMs;
            acceptComplete = true;
            let handle;
            try {
                const completion = new Promise(resolve => { complete = resolve; });
                await boundedCall("Tracing.end", {}, deadline);
                let id;
                try { handle = (await Promise.race([completion, new Promise((_, reject) => {
                    id = timer(() => reject(new Error("complete-timeout")), Math.max(1, deadline - now()));
                })])).stream; } finally { clear(id); }
                if (handle) await boundedCall("IO.close", { handle }, deadline);
            } catch {} finally { complete = undefined; acceptComplete = false; armed = false; }
        }
        if (processRecorder) {
            try {
                const metadata = await processRecorder.finish({ retain: Boolean(directory), directory });
                // Full bounded telemetry lives in processes.json, not duplicated here.
                processMetadata = { status: "finished", unavailable: metadata?.unavailable, nativeStatus: metadata?.nativeStatus,
                    nativeBytes: metadata?.nativeBytes, nativeTruncated: metadata?.nativeTruncated,
                    file: directory ? "processes.json" : undefined };
                record({ kind: "process-recorder-finished", status: processMetadata.nativeStatus ?? "telemetry-only" });
            } catch { processMetadata = { status: "finish-unavailable" }; record({ kind: "process-recorder-finish-unavailable" }); }
        }
        // Rewrite after slow-but-passing/failing verdict so it is not mislabeled.
        if (directory) {
            try { await writeFile(join(directory, "manifest.json"), JSON.stringify({ ...manifest(), verdict: failed ? "failed" : "passed", retained: reason }, null, 2) + "\n"); } catch {}
        }
        if (failed) {
            try { emit({ check: "browser-flight-recorder", sample, mode, reason, status, path: directory }); } catch {}
        }
        closed = true;
        for (const id of timers) cancel(id);
        timers.clear();
    }
    function finish(failed = false) {
        if (!finishPromise) finishPromise = finalize(failed).catch(() => {
            closed = true;
            for (const id of timers) cancel(id);
            timers.clear();
        });
        return finishPromise;
    }
    async function markQuarantined(policy, stillEligible = () => true) {
        await finish(true);
        if (!directory || status !== "saved" || truncated || bytes <= 0
            || policy?.status !== "inconclusive-known-timeout" || !stillEligible()) return undefined;
        try {
            const path = join(directory, "manifest.json");
            const saved = JSON.parse(await readFile(path, "utf8"));
            if (saved.verdict !== "failed" || saved.incomplete || saved.truncated) return undefined;
            const quarantine = { policyId: policy.policyId, status: policy.status, issue: policy.issue,
                expiry: policy.expiry, reason: policy.reason, occurrences: 1, remainingAssertionsExecuted: false,
                ...(environment.GITHUB_RUN_ID ? { runId: environment.GITHUB_RUN_ID } : {}),
                ...(environment.GITHUB_RUN_ATTEMPT ? { runAttempt: environment.GITHUB_RUN_ATTEMPT } : {}) };
            await writeFile(join(directory, "quarantine.json"), JSON.stringify(quarantine, null, 2) + "\n");
            if (!stillEligible()) {
                await writeFile(join(directory, "quarantine.json"), JSON.stringify({ ...quarantine, status: "revoked", occurrences: 0 }) + "\n");
                return undefined;
            }
            await writeFile(path, JSON.stringify({ ...saved, verdict: "inconclusive", quarantine }, null, 2) + "\n");
            if (!stillEligible()) {
                await writeFile(path, JSON.stringify(saved, null, 2) + "\n");
                await writeFile(join(directory, "quarantine.json"), JSON.stringify({ ...quarantine, status: "revoked", occurrences: 0 }) + "\n");
                return undefined;
            }
            return { ...quarantine, path: directory };
        } catch { return undefined; }
    }
    return { arm, dispatch, request, finish, markQuarantined, record, get status() { return status; } };
}

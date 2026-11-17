import { BrowserEvaluationFailure } from "./browser-known-timeout.mjs";
import { verifySoftwareWebGpu } from "./browser-gpu.mjs";

const expiry = "2026-12-01T00:00:00.000Z";
const introduced = Date.parse("2026-11-17T00:00:00.000Z");
const context = value => value?.stage === "connected-first-png" && value.pngIndex === 19;
const timed = event => Number.isFinite(event?.ticks) && event.ticks > 0
    && Number.isFinite(event.frequency) && event.frequency > 0;
const uuid = value => typeof value === "string" && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value)
    && value !== "00000000-0000-0000-0000-000000000000";
const sameFrame = (event, frame) => event?.data?.profileRunId === frame.profileRunId && event.data.frameId === frame.frameId;

// Classification only: never retries a capture, changes a deadline or hides other errors.
// Bounded reporter history may have dropped older frames; all decisive evidence must survive.
export function classifyBrowserTimeoutQuarantine({ environment, sequential, sameContent, stage, captureStage, pngIndex,
    failure, errors, audit, evidence, now = Date.now(), platform = process.platform } = {}) {
    if (platform !== "linux" || environment?.LUMYTE_QUARANTINE_CONNECTED_PNG_TIMEOUT !== "1"
        || environment?.LUMYTE_REQUIRE_SOFTWARE_WEBGPU !== "1"
        || sequential !== true || sameContent !== false || stage !== "remaining-transitions"
        || captureStage !== "connected-first-png" || pngIndex !== 19
        || !Number.isFinite(now) || now < introduced || now >= Date.parse(expiry)
        || !(failure instanceof BrowserEvaluationFailure)
        || failure.expression !== "globalThis.lumyteApplicationProbe.CapturePngAsync()"
        || failure.state !== "Running;Animating;"
        || failure.details?.exception?.className !== "ManagedError"
        || failure.details?.exception?.description !== "ManagedError: The automation deadline expired."
        || !Array.isArray(errors) || errors.length !== 0) return undefined;
    const r = evidence, s = r?.snapshot;
    if (r?.check !== "browser-capture-failure" || r.status !== "available" || r.invalid !== 0
        || r.stage !== stage || r.pngIndex !== pngIndex || r.truncated === true || s?.enabled !== true
        || s.truncated === true || !Array.isArray(r.captures) || !Array.isArray(r.gpu)
        || !Array.isArray(r.records) || !Array.isArray(s.events) || !Array.isArray(s.requests)
        || !Array.isArray(s.terminalFrames)) return undefined;
    const object = value => value !== null && typeof value === "object" && !Array.isArray(value);
    const validEvent = value => object(value) && timed(value) && object(value.data)
        && Number.isSafeInteger(value.eventId) && typeof value.name === "string";
    if (s.requests.some(e => !validEvent(e) || e.eventId !== 12 || e.name !== "CaptureState"
        || !Number.isSafeInteger(e.data.requestId) || e.data.requestId <= 0
        || !Number.isSafeInteger(e.data.phase) || !Number.isSafeInteger(e.data.reason))
        || s.terminalFrames.some(f => !object(f) || !Number.isSafeInteger(f.requestId) || f.requestId <= 0
            || !Number.isSafeInteger(f.frameId) || f.frameId <= 0 || !uuid(f.profileRunId)
            || f.observation !== "atTerminal" || !Array.isArray(f.openStages)
            || f.openStages.some(e => !validEvent(e) || e.eventId !== 1 || e.name !== "StageBegin"
                || !sameFrame(e, f) || !Number.isSafeInteger(e.data.stage) || e.data.stage <= 0
                || !Number.isSafeInteger(e.data.spanId) || e.data.spanId <= 0))) return undefined;
    const capture = r.captures.at(-1);
    if (capture?.index !== 19 || capture.stage !== captureStage || capture.status !== "rejected"
        || !Number.isFinite(capture.elapsedMs) || capture.elapsedMs < 30000 || capture.elapsedMs > 35000) return undefined;
    // Any GPU failure (including device loss), not just the pending call, is disqualifying.
    if (r.gpu.some(x => !x?.event || x.event.truncated === true
        || /lost|error|reject/i.test(x.event.method + " " + x.event.status))) return undefined;
    const gpu = r.gpu.at(-1);
    if (!gpu || !context(gpu) || gpu.event.method !== "onSubmittedWorkDone" || gpu.event.status !== "start"
        || !Number.isSafeInteger(gpu.event.id) || gpu.event.id <= 0
        || r.gpu.some(x => x.event.id === gpu.event.id && x.event.status !== "start")) return undefined;
    const terminals = s.requests.filter(e => e?.data?.requestId === 19);
    if (terminals.length !== 1) return undefined;
    const terminal = terminals[0];
    if (!timed(terminal) || terminal.name !== "CaptureState" || terminal.data.phase !== 9 || terminal.data.reason !== 1) return undefined;
    const events = [...s.events, ...r.records.map(x => x?.event), ...(Array.isArray(r.recent) ? r.recent : [])];
    if (events.some(e => !e || !e.data || !timed(e))) return undefined;
    const frames = s.terminalFrames.filter(f => f?.requestId === 19 && f.observation === "atTerminal"
        && Number.isSafeInteger(f.frameId) && f.frameId > 0 && uuid(f.profileRunId)
        && Array.isArray(f.openStages) && f.openStages.some(e => e?.data?.stage === 14));
    if (frames.length !== 1) return undefined;
    const frame = frames[0];
    const begins = frame.openStages.filter(e => e?.data?.stage === 14);
    if (begins.length !== 1) return undefined;
    const begin = begins[0];
    const beforeTerminal = e => timed(e) && e.ticks / e.frequency <= terminal.ticks / terminal.frequency;
    if (begin.name !== "StageBegin" || begin.eventId !== 1 || !sameFrame(begin, frame)
        || !Number.isSafeInteger(begin.data.spanId) || begin.data.spanId <= 0 || !beforeTerminal(begin)
        || !events.some(e => e?.name === "CaptureState" && e.data?.requestId === 19 && e.data.phase === 4
            && e.data.reason === 0 && sameFrame(e, frame) && beforeTerminal(e)
            && e.ticks / e.frequency <= begin.ticks / begin.frequency)
        || events.some(e => sameFrame(e, frame) && ((e.name === "StageEnd" && e.data.spanId === begin.data.spanId && beforeTerminal(e))
            || (e.name === "StageEnd" && e.data.outcome !== 0)
            || (e.name === "SurfaceResult" && e.data.status !== 0)))) return undefined;
    try { verifySoftwareWebGpu(audit); } catch { return undefined; }
    return {
        policyId: "connected-first-png-timeout-172", status: "inconclusive-known-timeout",
        issue: "https://github.com/ikihiki/Lumyte/issues/172", expiry,
        reason: "Sequential connected PNG 19 managed deadline with terminal frame awaiting GPU completion; not a correctness pass.",
    };
}

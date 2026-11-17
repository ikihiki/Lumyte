// Observe existing diagnostics without rendering, waiting for GPU work or normal-path console output.
export const captureTracePrefix = "UI Capture trace: ";
export const captureDiagnosticBinding = "__lumyteCaptureDiagnosticEvent";

export function captureDiagnosticsEnabled(environment = process.env) {
    return environment.LUMYTE_BROWSER_CAPTURE_TRACE === "1" || environment.LUMYTE_REQUIRE_SOFTWARE_WEBGPU === "1";
}
export function installCaptureDiagnosticsRequest(target = globalThis) { target.__lumyteCaptureDiagnosticsRequested = true; }

export function createCaptureTraceReporter({ emit = value => console.log(JSON.stringify(value)), maxRecords = 256, historyLimit = 32 } = {}) {
    let received = 0, invalid = 0, dropped = 0;
    const records = [], recent = [], gpu = [], captures = [];
    const safelyEmit = value => { try { emit(value); } catch {} };
    function retain(buffer, value, limit) {
        buffer.push(value);
        if (buffer.length > limit) { buffer.shift(); dropped++; }
    }
    function observe(text, context = {}) {
        if (typeof text !== "string" || !text.startsWith(captureTracePrefix)) return false;
        received++;
        let event;
        try {
            if (text.length > 16384) throw new Error("oversized");
            event = JSON.parse(text.slice(captureTracePrefix.length));
            if (!event || !Number.isInteger(event.eventId) || typeof event.name !== "string" || event.name.length > 128
                || !Number.isFinite(event.ticks) || !Number.isFinite(event.frequency) || event.frequency <= 0
                || !event.data || typeof event.data !== "object" || Array.isArray(event.data) || Object.keys(event.data).length > 48
                || Object.entries(event.data).some(([key, value]) => !/^[a-zA-Z][a-zA-Z0-9]*$/.test(key)
                    || !(value === null || typeof value === "boolean" || (typeof value === "number" && Number.isFinite(value))
                    || (typeof value === "string" && value.length <= 2048)))) throw new Error("shape");
        } catch { invalid++; return true; }
        event = { eventId: event.eventId, name: event.name, ticks: event.ticks, frequency: event.frequency, data: event.data };
        retain(records, { ...context, event }, maxRecords);
        recent.push(event);
        if (recent.length > historyLimit) recent.shift();
        return true;
    }
    function observeGpu(text, context = {}) {
        const prefix = "Browser GPU trace: ";
        if (typeof text !== "string" || !text.startsWith(prefix)) return false;
        received++;
        try {
            if (text.length > 4096) throw new Error("oversized");
            const value = JSON.parse(text.slice(prefix.length));
            if (!value || typeof value.method !== "string" || value.method.length > 80 || typeof value.status !== "string" || value.status.length > 32
                || !Number.isFinite(value.elapsedMs)) throw new Error("shape");
            const event = { method: value.method, status: value.status, elapsedMs: value.elapsedMs };
            if (Number.isSafeInteger(value.id)) event.id = value.id;
            if (Number.isFinite(value.atMs)) event.atMs = value.atMs;
            if (value.truncated === true) event.truncated = true;
            retain(gpu, { ...context, event }, maxRecords);
        } catch { invalid++; }
        return true;
    }
    function capture(value) { retain(captures, value, 100); }
    async function failure(command, context = {}) {
        let status = "unavailable", snapshot;
        try {
            if (typeof command === "function") {
                const result = await command("Runtime.evaluate", { expression: "globalThis.lumyteApplicationProbe?.GetCaptureDiagnostics?.()", returnByValue: true, awaitPromise: false }, 2000);
                const value = result?.result?.value;
                if (result?.exceptionDetails) status = "export-error";
                else if (typeof value !== "string") status = "not-enabled";
                else if (value.length > 131072) status = "snapshot-too-large";
                else { snapshot = JSON.parse(value); status = "available"; }
            }
        } catch { status = "snapshot-query-failed"; }
        // The only stdout path. Already-received buffers survive a hung/closed Browser.
        safelyEmit({ check: "browser-capture-failure", ...context, status, received, invalid, dropped, records: [...records], gpu: [...gpu], captures: [...captures], recent: [...recent], ...(snapshot === undefined ? {} : { snapshot }) });
    }
    return { observe, observeGpu, capture, failure, get received() { return received; }, get invalid() { return invalid; }, get dropped() { return dropped; } };
}

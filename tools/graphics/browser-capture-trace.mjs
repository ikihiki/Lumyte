// Observe the existing managed EventSource; never request extra renders or GPU work.
export const captureTracePrefix = "UI Capture trace: ";

export function captureDiagnosticsEnabled(environment = process.env) {
    return environment.LUMYTE_BROWSER_CAPTURE_TRACE === "1"
        || environment.LUMYTE_REQUIRE_SOFTWARE_WEBGPU === "1";
}

// Self-contained for injection before the managed application starts.
export function installCaptureDiagnosticsRequest(target = globalThis) {
    target.__lumyteCaptureDiagnosticsRequested = true;
}

export function createCaptureTraceReporter({ emit = value => console.log(JSON.stringify(value)), maxRecords = 5000, historyLimit = 32 } = {}) {
    let received = 0, invalid = 0, dropped = 0;
    const recent = [];
    const safelyEmit = value => { try { emit(value); } catch {} };
    function observe(text, context = {}) {
        if (typeof text !== "string" || !text.startsWith(captureTracePrefix)) return false;
        received++;
        let event;
        try {
            if (text.length > 16384) throw new Error("oversized");
            event = JSON.parse(text.slice(captureTracePrefix.length));
            if (!event || !Number.isInteger(event.eventId) || typeof event.name !== "string" || event.name.length > 128
                || !Number.isFinite(event.ticks) || !Number.isFinite(event.frequency) || event.frequency <= 0
                || !event.data || typeof event.data !== "object" || Array.isArray(event.data)
                || Object.keys(event.data).length > 48
                || Object.entries(event.data).some(([key, value]) => !/^[a-zA-Z][a-zA-Z0-9]*$/.test(key)
                    || !(value === null || typeof value === "boolean" || (typeof value === "number" && Number.isFinite(value))
                    || (typeof value === "string" && value.length <= 2048)))) throw new Error("shape");
        } catch {
            invalid++;
            if (invalid === 1) safelyEmit({ check: "browser-capture-diagnostics", ...context, status: "invalid-row" });
            return true;
        }
        // Project the declared schema; never forward unchecked top-level payloads.
        event = { eventId: event.eventId, name: event.name, ticks: event.ticks, frequency: event.frequency, data: event.data };
        recent.push(event);
        if (recent.length > historyLimit) recent.shift();
        if (received <= maxRecords) safelyEmit({ check: "browser-capture-event", ...context, event });
        else {
            dropped++;
            if (dropped === 1) safelyEmit({ check: "browser-capture-diagnostics", ...context, status: "trace-truncated", maxRecords });
        }
        return true;
    }
    async function failure(command, context = {}) {
        let status = "unavailable", snapshot;
        try {
            if (typeof command === "function") {
                const result = await command("Runtime.evaluate", {
                    expression: "globalThis.lumyteApplicationProbe?.GetCaptureDiagnostics?.()",
                    returnByValue: true, awaitPromise: false,
                }, 2000);
                const value = result?.result?.value;
                if (result?.exceptionDetails) status = "export-error";
                else if (typeof value !== "string") status = "not-enabled";
                else if (value.length > 131072) status = "snapshot-too-large";
                else { snapshot = JSON.parse(value); status = "available"; }
            }
        } catch { status = "snapshot-query-failed"; }
        // Emit even after the streaming budget is exhausted; preserve the original failure outside this observer.
        safelyEmit({ check: "browser-capture-failure", ...context, status, received, invalid, dropped, recent: [...recent], ...(snapshot === undefined ? {} : { snapshot }) });
    }
    return { observe, failure, get received() { return received; }, get invalid() { return invalid; }, get dropped() { return dropped; } };
}

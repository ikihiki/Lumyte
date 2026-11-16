// Only the Transitions verifier opts in; no production renderer configuration.
// Reuse the existing hosted software gate so the next CI run emits diagnostics
// without workflow/Build.Validation changes; local runs can opt in independently.
export function browserGpuTraceEnabled(environment = process.env) {
    return environment.LUMYTE_BROWSER_GPU_TRACE === "1"
        || environment.LUMYTE_REQUIRE_SOFTWARE_WEBGPU === "1";
}

// Self-contained for Page.addScriptToEvaluateOnNewDocument. Observe the actual
// application objects, never create work or wait for the queue ourselves. Promise
// observers return the original promise (not a chained/replaced application result).
// These are CPU wall intervals, not GPU timestamps. Observers add logging and a
// promise reaction; they cannot identify driver-internal phases. Rejection handlers
// observe failures without changing what the original application promise rejects
// with (but, like the adapter audit, attach a rejection observer to that promise).
// Stop after 2000 events, marking truncation; do not retain descriptors/resources.
export function installBrowserGpuTrace(target = globalThis) {
    const limit = 2000;
    let count = 0;
    let nextId = 0;
    const seen = new WeakSet();
    const safely = action => { try { return action(); } catch {} };
    const now = () => safely(() => target.performance.now()) ?? 0;
    const emit = value => safely(() => {
        if (count >= limit) return;
        count++;
        target.console?.info("Browser GPU trace:", JSON.stringify({ ...value,
            ...(count === limit ? { truncated: true } : {}) }));
    });
    function wrap(object, name, asyncResult = false, observe) {
        safely(() => {
            const original = object?.[name];
            if (typeof original !== "function") return;
            object[name] = function (...args) {
                const id = ++nextId;
                const start = now();
                emit({ id, method: name, status: "start", elapsedMs: 0, atMs: start });
                let result;
                try {
                    result = Reflect.apply(original, this, args);
                } catch (error) {
                    emit({ id, method: name, status: "throw", elapsedMs: now() - start });
                    throw error;
                }
                const finish = status => emit({ id, method: name, status, elapsedMs: now() - start, atMs: now() });
                if (asyncResult) {
                    safely(() => result.then(value => {
                        safely(() => observe?.(value));
                        finish("fulfilled");
                    }, () => { finish("rejected"); }));
                } else {
                    safely(() => observe?.(result));
                    finish("returned");
                }
                return result;
            };
        });
    }
    function buffer(value) {
        if (!value || seen.has(value)) return;
        seen.add(value);
        wrap(value, "mapAsync", true);
    }
    function device(value) {
        if (!value || seen.has(value)) return;
        seen.add(value);
        wrap(value, "createRenderPipeline");
        wrap(value, "createRenderPipelineAsync", true);
        // Do not log buffer creation itself; only install the map observer.
        safely(() => {
            const original = value.createBuffer;
            if (typeof original !== "function") return;
            value.createBuffer = function (...args) {
                const result = Reflect.apply(original, this, args);
                safely(() => buffer(result));
                return result;
            };
        });
        safely(() => {
            wrap(value.queue, "submit");
            wrap(value.queue, "onSubmittedWorkDone", true);
        });
    }
    function adapter(value) {
        if (!value || seen.has(value)) return;
        seen.add(value);
        wrap(value, "requestDevice", true, device);
    }
    safely(() => {
        const gpu = target.navigator?.gpu;
        emit({ method: "install", status: gpu ? "available" : "missing-gpu", elapsedMs: 0 });
        wrap(gpu, "requestAdapter", true, adapter);
    });
}

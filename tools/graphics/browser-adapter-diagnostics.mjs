// Observe the application's actual request; never probe again, replace options,
// choose a fallback, or change the returned promise / failure semantics.
export function observeAdapterRequests(gpu = globalThis.navigator?.gpu, report = details => console.info("Browser WebGPU adapter:", JSON.stringify(details))) {
    const emit = getDetails => {
        try { report(getDetails()); } catch { /* Diagnostics must not affect WebGPU. */ }
    };
    if (!gpu) {
        emit(() => ({ available: false }));
        return;
    }
    const requestAdapter = gpu.requestAdapter;
    gpu.requestAdapter = function (...args) {
        const request = Reflect.apply(requestAdapter, this, args);
        request.then(
            adapter => emit(() => ({
                available: true,
                options: args[0] ?? null,
                adapter: adapter ? {
                    vendor: adapter.info?.vendor,
                    architecture: adapter.info?.architecture,
                    device: adapter.info?.device,
                    description: adapter.info?.description,
                    isFallbackAdapter: adapter.info?.isFallbackAdapter,
                } : null,
            })),
            error => emit(() => ({ available: true, options: args[0] ?? null, error: String(error) })),
        );
        return request;
    };
}

import assert from "node:assert/strict";

// Keep ANGLE and Dawn on the same bundled software implementation on Linux.
// Windows retains each verifier's existing backend choice.
export function browserGpuArguments(platform = process.platform, windowsBackend = "d3d11") {
    const enabled = ["--enable-unsafe-webgpu", "--enable-unsafe-swiftshader"];
    if (platform === "win32") {
        if (windowsBackend === "d3d11") return [...enabled, "--use-angle=d3d11"];
        assert.equal(windowsBackend, "swiftshader", "Unknown Windows verifier backend.");
        return [...enabled, "--enable-features=Vulkan", "--use-vulkan=swiftshader", "--use-angle=swiftshader", "--disable-vulkan-surface"];
    }
    return [
        ...enabled, "--enable-features=Vulkan", "--use-vulkan=swiftshader",
        "--use-webgpu-adapter=swiftshader", "--use-angle=swiftshader",
        "--disable-vulkan-surface",
    ];
}

export function requiresSoftwareWebGpu(environment = process.env, platform = process.platform) {
    return platform === "linux" && environment.LUMYTE_REQUIRE_SOFTWARE_WEBGPU === "1";
}

// Serialized into the page before its own code runs. Observe actual application
// requests; do not probe again, change options/promises, retry, or choose a fallback.
export function installBrowserGpuAudit(target = globalThis) {
    const gpu = target.navigator?.gpu;
    const audit = { available: Boolean(gpu), requests: [] };
    target.__lumyteBrowserGpuAudit = audit;
    if (!gpu) return audit;
    const requestAdapter = gpu.requestAdapter;
    gpu.requestAdapter = function (...args) {
        const request = Reflect.apply(requestAdapter, this, args);
        request.then(adapter => {
            try {
                const info = adapter?.info;
                const entry = { adapter: adapter ? {
                    vendor: info?.vendor,
                    architecture: info?.architecture,
                    device: info?.device,
                    description: info?.description,
                    isFallbackAdapter: info?.isFallbackAdapter,
                } : null };
                audit.requests.push(entry);
                try { target.console?.info("Browser WebGPU adapter:", JSON.stringify(entry)); } catch {}
            } catch (error) {
                audit.requests.push({ error: String(error) });
            }
        }, error => { audit.requests.push({ error: String(error) }); });
        return request;
    };
    return audit;
}

export function verifySoftwareWebGpu(audit) {
    assert.equal(audit?.available, true, "Software WebGPU requires navigator.gpu.");
    assert.ok(Array.isArray(audit.requests) && audit.requests.length > 0,
        "No application WebGPU adapter request was observed.");
    for (const request of audit.requests) {
        const info = request.adapter;
        assert.ok(info?.vendor === "google" && info.architecture === "swiftshader" && info.isFallbackAdapter === true,
            "Expected the bundled SwiftShader software adapter, not hardware/ANGLE status: " + JSON.stringify(request));
    }
    console.log("Software WebGPU verified: " + JSON.stringify(audit.requests));
}

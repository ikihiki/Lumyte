import assert from "node:assert/strict";
import test from "node:test";
import vm from "node:vm";
import { browserGpuArguments, requiresSoftwareWebGpu, installBrowserGpuAudit, verifySoftwareWebGpu } from "./browser-gpu.mjs";

const software = { info: { vendor: "google", architecture: "swiftshader", isFallbackAdapter: true } };

test("Linux selects bundled SwiftShader for both ANGLE and Dawn", () => {
    const flags = browserGpuArguments("linux");
    for (const flag of ["--enable-unsafe-webgpu", "--enable-unsafe-swiftshader", "--use-angle=swiftshader", "--use-vulkan=swiftshader", "--use-webgpu-adapter=swiftshader"])
        assert.ok(flags.includes(flag), flag);
    assert.ok(!flags.includes("--use-angle=vulkan"));
    assert.equal(flags.filter(flag => flag.startsWith("--use-angle=")).length, 1);
    flags.push("changed");
    assert.ok(!browserGpuArguments("linux").includes("changed"));
});

test("Windows retains its existing D3D host path", () => {
    assert.deepEqual(browserGpuArguments("win32"), ["--enable-unsafe-webgpu", "--enable-unsafe-swiftshader", "--use-angle=d3d11"]);
    assert.deepEqual(browserGpuArguments("win32", "swiftshader"), ["--enable-unsafe-webgpu", "--enable-unsafe-swiftshader", "--enable-features=Vulkan", "--use-vulkan=swiftshader", "--use-angle=swiftshader", "--disable-vulkan-surface"]);
    assert.throws(() => browserGpuArguments("win32", "unknown"), /Unknown Windows/);
});

test("strict software verification is explicit and Linux-only", () => {
    assert.equal(requiresSoftwareWebGpu({}, "linux"), false);
    assert.equal(requiresSoftwareWebGpu({ LUMYTE_REQUIRE_SOFTWARE_WEBGPU: "1" }, "linux"), true);
    assert.equal(requiresSoftwareWebGpu({ LUMYTE_REQUIRE_SOFTWARE_WEBGPU: "1" }, "win32"), false);
});

for (const adapter of [software, null, { info: { vendor: "nvidia", architecture: "desktop", isFallbackAdapter: false } }]) {
    test("observes application request without retry, changed options, receiver, or promise: " + (adapter?.info?.vendor ?? "null"), async () => {
        const options = { powerPreference: "high-performance" };
        const promise = Promise.resolve(adapter);
        let calls = 0;
        const gpu = { requestAdapter(value) {
            assert.equal(this, gpu);
            assert.equal(value, options);
            calls++;
            return promise;
        } };
        const audit = installBrowserGpuAudit({ navigator: { gpu } });
        assert.equal(calls, 0);
        assert.equal(gpu.requestAdapter(options), promise);
        assert.equal(await promise, adapter);
        assert.equal(calls, 1);
        assert.equal(audit.requests.length, 1);
        if (adapter === software) verifySoftwareWebGpu(audit);
        else assert.throws(() => verifySoftwareWebGpu(audit), /Expected the bundled SwiftShader/);
    });
}

test("missing WebGPU and no application request cannot pass", () => {
    assert.throws(() => verifySoftwareWebGpu(installBrowserGpuAudit({})), /requires navigator.gpu/);
    const gpu = { requestAdapter() { assert.fail("must not issue a preflight request"); } };
    assert.throws(() => verifySoftwareWebGpu(installBrowserGpuAudit({ navigator: { gpu } })), /No application/);
});

test("ANGLE SystemInfo or incomplete adapter info cannot substitute for a real software adapter", () => {
    for (const adapter of [{ vendor: "google", architecture: "swiftshader" }, { vendor: "google", architecture: "other", isFallbackAdapter: true }, { devices: [{ deviceString: "SwiftShader" }] }])
        assert.throws(() => verifySoftwareWebGpu({ available: true, requests: [{ adapter }] }), /Expected the bundled SwiftShader/);
});

test("adapter failure preserves original rejection without retry", async () => {
    const error = new Error("software driver unavailable");
    const promise = Promise.reject(error);
    let calls = 0;
    const gpu = { requestAdapter() { calls++; return promise; } };
    const audit = installBrowserGpuAudit({ navigator: { gpu } });
    assert.equal(gpu.requestAdapter(), promise);
    await assert.rejects(promise, value => value === error);
    assert.equal(calls, 1);
    assert.throws(() => verifySoftwareWebGpu(audit), /software driver unavailable/);
});

test("synchronous adapter errors are not replaced", () => {
    const error = new Error("invalid invocation");
    const gpu = { requestAdapter() { throw error; } };
    installBrowserGpuAudit({ navigator: { gpu } });
    assert.throws(() => gpu.requestAdapter(), value => value === error);
});

test("console diagnostics do not alter application success", async () => {
    const promise = Promise.resolve(software);
    const gpu = { requestAdapter() { return promise; } };
    const audit = installBrowserGpuAudit({ navigator: { gpu }, console: { info() { throw new Error("console unavailable"); } } });
    assert.equal(gpu.requestAdapter(), promise);
    assert.equal(await promise, software);
    verifySoftwareWebGpu(audit);
});

test("page injection is self-contained and sees the real application request", async () => {
    const gpu = { requestAdapter() { return Promise.resolve(software); } };
    const page = { navigator: { gpu } };
    vm.runInNewContext(`(${installBrowserGpuAudit.toString()})();`, page);
    await gpu.requestAdapter();
    verifySoftwareWebGpu(page.__lumyteBrowserGpuAudit);
});

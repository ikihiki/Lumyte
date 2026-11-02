import assert from "node:assert/strict";
import test from "node:test";
import { observeAdapterRequests } from "./browser-adapter-diagnostics.mjs";

for (const adapter of [null, { info: { vendor: "google", architecture: "swiftshader", isFallbackAdapter: true } }]) {
    test("observes " + (adapter ? "an adapter" : "null") + " without another request or changing the promise", async () => {
        const reports = [];
        const options = { powerPreference: "high-performance" };
        const promise = Promise.resolve(adapter);
        let calls = 0;
        const gpu = { requestAdapter(value) {
            assert.equal(this, gpu);
            assert.equal(value, options);
            calls++;
            return promise;
        } };
        observeAdapterRequests(gpu, details => reports.push(details));
        assert.equal(calls, 0);
        assert.equal(gpu.requestAdapter(options), promise);
        assert.equal(await promise, adapter);
        assert.equal(calls, 1);
        assert.equal(reports.length, 1);
        assert.equal(reports[0].options, options);
        assert.deepEqual(reports[0].adapter, adapter ? { vendor: "google", architecture: "swiftshader", device: undefined, description: undefined, isFallbackAdapter: true } : null);
    });
}

test("preserves rejected promise and reports failure without retry", async () => {
    const error = new Error("adapter initialization failed");
    const promise = Promise.reject(error);
    const reports = [];
    let calls = 0;
    const gpu = { requestAdapter() { calls++; return promise; } };
    observeAdapterRequests(gpu, details => reports.push(details));
    assert.equal(gpu.requestAdapter(), promise);
    await assert.rejects(promise, value => value === error);
    assert.equal(calls, 1);
    assert.equal(reports[0].error, String(error));
});

test("preserves synchronous exceptions", () => {
    const error = new Error("invalid invocation");
    const gpu = { requestAdapter() { throw error; } };
    observeAdapterRequests(gpu, () => assert.fail("must not report success"));
    assert.throws(() => gpu.requestAdapter(), value => value === error);
});

test("report failures do not affect application result", async () => {
    const promise = Promise.resolve(null);
    const gpu = { requestAdapter() { return promise; } };
    observeAdapterRequests(gpu, () => { throw new Error("diagnostics failed"); });
    assert.equal(gpu.requestAdapter(), promise);
    assert.equal(await promise, null);
});

test("reports missing WebGPU without acquiring anything", () => {
    const reports = [];
    observeAdapterRequests(null, details => reports.push(details));
    assert.deepEqual(reports, [{ available: false }]);
});

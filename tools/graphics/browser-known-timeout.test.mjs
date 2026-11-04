import assert from "node:assert/strict";
import test from "node:test";
import { BrowserEvaluationFailure, shouldIgnoreKnownBrowserTimeout } from "./browser-known-timeout.mjs";
import { installBrowserGpuAudit, verifySoftwareWebGpu } from "./browser-gpu.mjs";

const expression = "globalThis.lumyteApplicationProbe.CapturePngAsync()";
const state = "Running;Animating;";
const description = "ManagedError: The automation deadline expired.";

function knownFailure(overrides = {}) {
    return new BrowserEvaluationFailure(description, {
        expression,
        state,
        details: { exception: { className: "ManagedError", description } },
        ...overrides,
    });
}

function softwareAudit() {
    return { available: true, requests: [{ adapter: {
        vendor: "google", architecture: "swiftshader", isFallbackAdapter: true,
    } }] };
}

function candidate(overrides = {}) {
    return {
        environment: { LUMYTE_ALLOW_KNOWN_BROWSER_TIMEOUT: "1" },
        sequential: true,
        sameContent: false,
        stage: "entry-midpoint-png",
        failure: knownFailure(),
        errors: [],
        audit: softwareAudit(),
        ...overrides,
    };
}

function rejects(name, overrides) {
    test(name, () => {
        assert.equal(shouldIgnoreKnownBrowserTimeout(candidate(overrides)), false);
    });
}

test("BrowserEvaluationFailure preserves Error identity and structured evidence", () => {
    const details = { exception: { className: "ManagedError", description } };
    const failure = new BrowserEvaluationFailure("evaluation failed", { expression, state, details });
    assert.ok(failure instanceof Error);
    assert.ok(failure instanceof BrowserEvaluationFailure);
    assert.equal(failure.message, "evaluation failed");
    assert.equal(failure.expression, expression);
    assert.equal(failure.state, state);
    assert.equal(failure.details, details);
});

test("explicit allowance accepts only known managed deadline with actual application software audit", async () => {
    let requests = 0;
    const gpu = { requestAdapter() {
        requests++;
        return Promise.resolve({ info: {
            vendor: "google", architecture: "swiftshader", isFallbackAdapter: true,
        } });
    } };
    const audit = installBrowserGpuAudit({ navigator: { gpu } });
    await gpu.requestAdapter();
    verifySoftwareWebGpu(audit);
    assert.equal(requests, 1);
    assert.equal(shouldIgnoreKnownBrowserTimeout(candidate({ audit })), true);
    assert.equal(requests, 1, "classification must not request another adapter");
});

for (const value of [undefined, "true", "0", "", true, 1]) {
    rejects("flag must be the exact string 1: " + String(value), {
        environment: value === undefined ? {} : { LUMYTE_ALLOW_KNOWN_BROWSER_TIMEOUT: value },
    });
}

for (const sequential of [undefined, false, "true", 1])
    rejects("default/nonsequential modes cannot opt out: " + String(sequential), { sequential });
for (const sameContent of [true, undefined, "false", 0])
    rejects("same-content must be explicitly false: " + String(sameContent), { sameContent });
for (const stage of [undefined, "entry-png", "entry-start-png", "entry-final-png", "exit-midpoint-png", "entry-midpoint-png-later"])
    rejects("earlier, later, or unknown stage remains fatal: " + String(stage), { stage });

rejects("pixel assertion failures remain fatal", {
    failure: new assert.AssertionError({ message: "pixel mismatch: " + description }),
});
rejects("generic Error with identical deadline text remains fatal", { failure: new Error(description) });
rejects("generic CDP evaluation rejection remains fatal", { failure: new Error("Runtime.evaluate: " + description) });
rejects("thrown deadline string remains fatal", { failure: description });
rejects("plain object cannot impersonate structured evaluation failure", {
    failure: { message: description, expression, state, details: { exception: { className: "ManagedError", description } } },
});
for (const other of ["globalThis.lumyteApplicationProbe.WaitForIdleAsync()", expression + ";", undefined])
    rejects("other evaluation expression remains fatal: " + String(other), { failure: knownFailure({ expression: other }) });
for (const other of ["Running;", "Animating;", "Running;Animating;Failed;", undefined])
    rejects("other application state remains fatal: " + String(other), { failure: knownFailure({ state: other }) });
for (const className of ["Error", "TimeoutError", "ManagedErrorSubclass", undefined])
    rejects("other exception class remains fatal: " + String(className), {
        failure: knownFailure({ details: { exception: { className, description } } }),
    });
for (const other of ["The automation deadline expired.", description + " extra", "ManagedError: A timeout occurred.", undefined])
    rejects("other exception description remains fatal: " + String(other), {
        failure: knownFailure({ details: { exception: { className: "ManagedError", description: other } } }),
    });
for (const details of [undefined, {}, { text: description }, { exception: null }])
    rejects("missing exception evidence remains fatal: " + JSON.stringify(details), { failure: knownFailure({ details }) });

for (const errors of [["GPU device lost"], [new Error("browser page error")], [description], undefined, null, {}])
    rejects("concurrent browser/GPU errors or absent error collection remain fatal: " + String(errors), { errors });

const invalidAudits = [
    undefined,
    null,
    {},
    { available: false, requests: softwareAudit().requests },
    { available: true, requests: [] },
    { available: true },
    { available: true, requests: [{ adapter: null }] },
    { available: true, requests: [{ error: "software driver unavailable" }] },
    { available: true, requests: [{ adapter: { vendor: "nvidia", architecture: "desktop", isFallbackAdapter: false } }] },
    { available: true, requests: [{ adapter: { vendor: "google", architecture: "other", isFallbackAdapter: true } }] },
    { available: true, requests: [{ adapter: { vendor: "google", architecture: "swiftshader", isFallbackAdapter: false } }] },
    { available: true, requests: [{ adapter: { vendor: "google", architecture: "swiftshader" } }] },
    { available: true, devices: [{ deviceString: "SwiftShader" }], requests: [] },
    { available: true, requests: [...softwareAudit().requests, { error: "second application request failed" }] },
];
for (const [index, audit] of invalidAudits.entries())
    rejects("missing, wrong-driver, failed, or non-application software audit remains fatal: " + index, { audit });

for (const failure of [undefined, null]) {
    test("legitimate success without failure is not classified as an ignored timeout: " + String(failure), () => {
        const input = candidate({ failure });
        const before = structuredClone(input);
        assert.equal(shouldIgnoreKnownBrowserTimeout(input), false);
        assert.deepEqual(input, before, "successful input remains unchanged");
    });
}

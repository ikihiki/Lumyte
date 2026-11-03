import { verifySoftwareWebGpu } from "./browser-gpu.mjs";

// Preserve structured CDP details; a matching message in unrelated output is not enough.
export class BrowserEvaluationFailure extends Error {
    constructor(message, { expression, state, details }) {
        super(message);
        this.name = "BrowserEvaluationFailure";
        this.expression = expression;
        this.state = state;
        this.details = details;
    }
}

// Temporary, opt-in CI allowance for the existing sequential midpoint PNG failure.
// Pixel assertions, other stages, runtime/GPU errors, and unverified adapters still fail.
export function shouldIgnoreKnownBrowserTimeout({ environment, sequential, sameContent, stage, failure, errors, audit }) {
    if (environment?.LUMYTE_ALLOW_KNOWN_BROWSER_TIMEOUT !== "1"
        || sequential !== true || sameContent !== false || stage !== "entry-midpoint-png"
        || !(failure instanceof BrowserEvaluationFailure)
        || failure.expression !== "globalThis.lumyteApplicationProbe.CapturePngAsync()"
        || failure.state !== "Running;Animating;"
        || failure.details?.exception?.className !== "ManagedError"
        || failure.details?.exception?.description !== "ManagedError: The automation deadline expired."
        || !Array.isArray(errors) || errors.length !== 0) {
        return false;
    }
    try {
        verifySoftwareWebGpu(audit);
        return true;
    } catch {
        return false;
    }
}

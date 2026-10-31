let animationEpoch = 0;
for (const type of ["visibilitychange", "freeze", "resume"]) {
    document.addEventListener(type, () => { animationEpoch++; });
}
export function animationGeneration() { return animationEpoch; }
export function canvas(id) {
    const result = document.getElementById(id);
    if (!(result instanceof HTMLCanvasElement)) throw new Error("platform-unavailable: canvas not found");
    return result;
}
export function context(canvas) {
    const result = canvas.getContext("webgpu");
    if (!result) throw new Error("graphics-initialize-failed: WebGPU unavailable");
    return result;
}
export function connected(canvas) { return canvas.isConnected; }
export function visible(canvas) { return canvas.isConnected && !document.hidden; }
export function nextTurn(milliseconds) {
    return new Promise(resolve => {
        let frame = 0;
        let timer = 0;
        const done = () => {
            if (frame) cancelAnimationFrame(frame);
            if (timer) clearTimeout(timer);
            resolve();
        };
        if (!document.hidden) frame = requestAnimationFrame(done);
        // Maintenance continues while hidden or when the browser temporarily delays rAF.
        timer = setTimeout(done, document.hidden ? Math.max(10, milliseconds) : Math.max(100, milliseconds));
    });
}

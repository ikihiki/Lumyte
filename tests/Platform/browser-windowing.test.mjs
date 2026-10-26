import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

class Canvas {
    constructor() {
        this.attributes = new Map();
        this.style = {};
        this.width = 300;
        this.height = 150;
        this.tabIndex = -1;
        this.isConnected = true;
        this.listeners = new Map();
        this.gpu = {};
    }
    get clientWidth() { return Number.parseFloat(this.style.width) || 300; }
    get clientHeight() { return Number.parseFloat(this.style.height) || 150; }
    getContext(name) { return name === "webgpu" ? this.gpu : null; }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    setAttribute(name, value) { this.attributes.set(name, value); }
    removeAttribute(name) { this.attributes.delete(name); }
    addEventListener(name, callback) {
        const callbacks = this.listeners.get(name) ?? new Set();
        callbacks.add(callback);
        this.listeners.set(name, callbacks);
    }
    removeEventListener(name, callback) { this.listeners.get(name)?.delete(callback); }
    dispatch(name, event = {}) { for (const callback of this.listeners.get(name) ?? []) callback(event); }
    getClientRects() { return this.style.display === "none" ? [] : [this.getBoundingClientRect()]; }
    getBoundingClientRect() { return { left: 10, top: 20, width: this.clientWidth, height: this.clientHeight }; }
    focus() { document.activeElement = this; this.dispatch("focus"); }
}

globalThis.HTMLCanvasElement = Canvas;
globalThis.document = { activeElement: null, hasFocus: () => true };
globalThis.window = new Canvas();
window.devicePixelRatio = 2;
globalThis.ResizeObserver = class {
    constructor(callback) { this.callback = callback; }
    observe() {}
    disconnect() { this.disconnected = true; }
};
globalThis.getComputedStyle = () => ({ visibility: "visible" });
const code = await readFile(new URL("../../src/Platform/Lumyte.Platform.Browser/wwwroot/lumyte-windowing.js", import.meta.url), "utf8");
const api = await import(`data:text/javascript;base64,${Buffer.from(code).toString("base64")}`);

test("canvas attachment retains its existing WebGPU context and prevents duplicate ownership", () => {
    const canvas = new Canvas();
    canvas.setAttribute("style", "color: red");
    const handle = api.attach(canvas, "test", 400, 300, false);
    assert.equal(api.context(handle), canvas.gpu);
    assert.deepEqual(api.snapshot(handle).slice(0, 6), [400, 300, 800, 600, 2, 0]);
    assert.throws(() => api.attach(canvas, "duplicate", 1, 1, false));
    api.show(handle, true);
    assert.equal(api.snapshot(handle)[5], 1);
    assert.equal(api.activate(handle), true);
    api.resize(handle, 100, 50);
    assert.deepEqual(api.snapshot(handle).slice(0, 4), [100, 50, 200, 100]);
    api.detach(handle);
    api.detach(handle);
    assert.equal(canvas.getAttribute("style"), "color: red");
    assert.equal(canvas.width, 300);
    assert.equal(canvas.height, 150);
    assert.equal([...canvas.listeners.values()].reduce((count, set) => count + set.size, 0), 0);
});

test("nonactivating displays preserve focus and DOM input is drained once in receive order", () => {
    const canvas = new Canvas();
    const originalFocus = document.activeElement;
    const handle = api.attach(canvas, "popup host", 100, 50, true);
    api.show(handle, true);
    assert.equal(api.activate(handle), false);
    assert.equal(document.activeElement, originalFocus);
    canvas.dispatch("keydown", { code: "KeyA", repeat: false });
    canvas.dispatch("mousemove", { clientX: 30, clientY: 25 });
    canvas.dispatch("wheel", { deltaX: 60, deltaY: 120, deltaMode: 0 });
    const events = JSON.parse(api.drainInput(handle));
    assert.deepEqual(events.map(e => e.type), [0, 1, 3]);
    assert.equal(events[0].code, "KeyA");
    assert.equal(events[1].x, 20);
    assert.equal(events[2].y, -1);
    assert.deepEqual(JSON.parse(api.drainInput(handle)), []);
    canvas.isConnected = false;
    assert.equal(api.snapshot(handle)[7], 0);
    api.detach(handle);
});

test("browser clipboard capabilities and permission rejection are explicit", async () => {
    Object.defineProperty(globalThis, "navigator", { configurable: true, value: {
        clipboard: { readText: async () => "日本語", writeText: async () => { throw new Error("permission denied"); } }
    } });
    globalThis.isSecureContext = false;
    assert.equal(api.hasClipboard(), false);
    globalThis.isSecureContext = true;
    assert.equal(api.hasClipboard(), true);
    assert.equal(await api.readClipboard(), "日本語");
    await assert.rejects(api.writeClipboard("x"), /permission denied/);
});

test("preserve-focus canvases cannot take focus and outside mouse releases are retained", () => {
    const canvas = new Canvas();
    canvas.tabIndex = 0;
    canvas.setAttribute("tabindex", "0");
    const prior = {};
    document.activeElement = prior;
    const handle = api.attach(canvas, "popup", 100, 50, true);
    api.show(handle, true);
    let prevented = false;
    canvas.dispatch("mousedown", { button: 0, preventDefault: () => { prevented = true; } });
    window.dispatch("mouseup", { button: 0 });
    assert.equal(prevented, true);
    assert.equal(canvas.tabIndex, -1);
    assert.equal(document.activeElement, prior);
    assert.deepEqual(JSON.parse(api.drainInput(handle)).filter(event => event.type === 2).map(event => event.down), [true, false]);
    document.hasFocus = () => false;
    document.activeElement = canvas;
    assert.equal(api.snapshot(handle)[6], 0);
    document.hasFocus = () => true;
    api.detach(handle);
    assert.equal(canvas.getAttribute("tabindex"), "0");
});

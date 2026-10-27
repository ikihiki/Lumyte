const owners = new WeakMap();

export function attach(canvas, title, width, height, preserveFocus) {
    if (!(canvas instanceof HTMLCanvasElement) || !canvas.isConnected) {
        throw new Error("Provide a connected HTMLCanvasElement.");
    }
    if (owners.has(canvas)) throw new Error("Canvas is already attached.");
    const gpu = canvas.getContext("webgpu");
    if (!gpu) throw new Error("WebGPU canvas context is unavailable.");
    const saved = {
        style: canvas.getAttribute("style"),
        title: canvas.getAttribute("title"),
        tabIndex: canvas.getAttribute("tabindex"),
        width: canvas.width,
        height: canvas.height
    };
    const handle = { canvas, gpu, saved, width, height, preserveFocus, visible: false, events: [], input: [], buttons: new Set(), detached: false };
    handle.capture = () => {
        if (!handle.detached) handle.events.push(...snapshot(handle));
    };
    handle.listeners = [];
    const listen = (name, callback) => {
        canvas.addEventListener(name, callback);
        handle.listeners.push([name, callback]);
    };
    handle.windowListeners = [];
    const listenWindow = (name, callback) => {
        window.addEventListener(name, callback);
        handle.windowListeners.push([name, callback]);
    };
    listen("keydown", event => handle.input.push({ type: 0, code: event.code, down: true, repeat: event.repeat }));
    listen("keyup", event => handle.input.push({ type: 0, code: event.code, down: false, repeat: false }));
    listen("mousemove", event => {
        const rect = canvas.getBoundingClientRect();
        handle.input.push({ type: 1, x: (event.clientX - rect.left) * canvas.clientWidth / rect.width, y: (event.clientY - rect.top) * canvas.clientHeight / rect.height });
    });
    listen("mousedown", event => {
        if (handle.preserveFocus) event.preventDefault();
        handle.buttons.add(event.button);
        handle.input.push({ type: 2, x: event.button, down: true });
    });
    listen("mouseup", event => {
        handle.buttons.delete(event.button);
        handle.input.push({ type: 2, x: event.button, down: false });
    });
    listenWindow("mouseup", event => {
        if (handle.buttons.delete(event.button)) handle.input.push({ type: 2, x: event.button, down: false });
    });
    listen("wheel", event => {
        const divisor = event.deltaMode === 0 ? 120 : event.deltaMode === 1 ? 3 : 1;
        handle.input.push({ type: 3, x: event.deltaX / divisor, y: -event.deltaY / divisor });
    });
    listen("focus", () => handle.input.push({ type: 4, down: true }));
    listen("blur", () => handle.input.push({ type: 4, down: false }));
    canvas.title = title;
    if (preserveFocus) canvas.tabIndex = -1;
    else if (canvas.tabIndex < 0) canvas.tabIndex = 0;
    canvas.style.display = "none";
    canvas.style.width = `${width}px`;
    canvas.style.height = `${height}px`;
    canvas.addEventListener("focus", handle.capture);
    canvas.addEventListener("blur", handle.capture);
    window.addEventListener("resize", handle.capture);
    handle.observer = new ResizeObserver(handle.capture);
    handle.observer.observe(canvas);
    owners.set(canvas, handle);
    snapshot(handle);
    return handle;
}

export function context(handle) { return handle.gpu; }

export function snapshot(handle) {
    const { canvas } = handle;
    const connected = canvas.isConnected && !handle.detached;
    const visible = connected && handle.visible && canvas.getClientRects().length > 0 && getComputedStyle(canvas).visibility !== "hidden";
    const width = visible ? canvas.clientWidth : handle.width;
    const height = visible ? canvas.clientHeight : handle.height;
    const scale = window.devicePixelRatio || 1;
    const pixelWidth = Math.max(1, Math.round(width * scale));
    const pixelHeight = Math.max(1, Math.round(height * scale));
    if (canvas.width !== pixelWidth) canvas.width = pixelWidth;
    if (canvas.height !== pixelHeight) canvas.height = pixelHeight;
    return [width, height, pixelWidth, pixelHeight, scale, visible ? 1 : 0, document.hasFocus() && document.activeElement === canvas ? 1 : 0, connected ? 1 : 0];
}

export function drain(handle) {
    const result = handle.events;
    handle.events = [];
    return result;
}

export function show(handle, visible) {
    handle.visible = visible;
    handle.canvas.style.display = visible ? "block" : "none";
    if (visible && !handle.preserveFocus) activate(handle);
    handle.capture();
}

export function resize(handle, width, height) {
    handle.width = width;
    handle.height = height;
    handle.canvas.style.width = `${width}px`;
    handle.canvas.style.height = `${height}px`;
    handle.capture();
}

export function title(handle, value) { handle.canvas.title = value; }

export function activate(handle) {
    if (handle.preserveFocus || !handle.visible) return false;
    handle.canvas.focus({ preventScroll: true });
    return document.activeElement === handle.canvas;
}

export function detach(handle) {
    if (handle.detached) return;
    handle.detached = true;
    handle.observer.disconnect();
    for (const [name, callback] of handle.listeners) handle.canvas.removeEventListener(name, callback);
    for (const [name, callback] of handle.windowListeners) window.removeEventListener(name, callback);
    handle.canvas.removeEventListener("focus", handle.capture);
    handle.canvas.removeEventListener("blur", handle.capture);
    window.removeEventListener("resize", handle.capture);
    for (const name of ["style", "title", "tabindex"]) {
        const key = name === "tabindex" ? "tabIndex" : name;
        const value = handle.saved[key];
        if (value === null) handle.canvas.removeAttribute(name);
        else handle.canvas.setAttribute(name, value);
    }
    handle.canvas.width = handle.saved.width;
    handle.canvas.height = handle.saved.height;
    owners.delete(handle.canvas);
    handle.events.length = 0;
    handle.input.length = 0;
}

export function hasClipboard() {
    return !!(globalThis.isSecureContext && navigator.clipboard?.readText && navigator.clipboard?.writeText);
}

export function readClipboard() { return navigator.clipboard.readText(); }

export function writeClipboard(text) { return navigator.clipboard.writeText(text); }

export function drainInput(handle) {
    return JSON.stringify(handle.input.splice(0));
}

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
    handle.keyboardHeld = new Set();
    handle.textConsumedKeys = new Set();
    handle.proxies = new Map();
    handle.proxyListeners = new Map();
    handle.logicalFocus = false;
    handle.ownsFocus = element => element === canvas || [...handle.proxies.values()].includes(element);
    handle.inputEnabled = true;
    const record = (...events) => {
        if (!handle.inputEnabled || handle.inputFault) return;
        if (handle.input.length + events.length > 16384) {
            handle.inputFault = true;
            handle.cancelMouse();
            return;
        }
        handle.input.push(...events);
    };
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
    listen("keydown", event => {
        handle.keyboardHeld.add(event.code);
        if (handle.textConsumedKeys.has(event.code)) { event.preventDefault(); return; }
        record({ type: 0, code: event.code, down: true, repeat: event.repeat });
    });
    listen("keyup", event => {
        handle.keyboardHeld.delete(event.code);
        if (handle.textConsumedKeys.delete(event.code)) { event.preventDefault(); return; }
        record({ type: 0, code: event.code, down: false, repeat: false });
    });
    listenWindow("keydown", event => { if (handle.ownsFocus(event.target)) handle.keyboardHeld.add(event.code); });
    listenWindow("keyup", event => { handle.keyboardHeld.delete(event.code); handle.textConsumedKeys.delete(event.code); });
    // Capture is acquired during the physical Down, before any managed frame consumes input.
    let mousePointer = null;
    let physicalButtons = 0;
    let blocked = false;
    const masks = [[1, 0], [2, 2], [4, 1], [8, 3], [16, 4]];
    const cancelMouse = () => {
        const pointer = mousePointer;
        mousePointer = null;
        blocked = physicalButtons !== 0;
        for (const button of handle.buttons) record({ type: 5, x: button });
        handle.buttons.clear();
        if (pointer !== null && canvas.hasPointerCapture(pointer)) canvas.releasePointerCapture(pointer);
    };
    handle.cancelMouse = cancelMouse;
    const mouse = event => {
        if (event.pointerType !== "mouse") return;
        physicalButtons = event.buttons;
        if (!handle.inputEnabled) {
            cancelMouse();
            return;
        }
        if (event.type === "pointercancel") {
            cancelMouse();
            return;
        }
        if (blocked) {
            if (physicalButtons === 0) blocked = false;
            return;
        }
        const rect = canvas.getBoundingClientRect();
        if (!(rect.width > 0 && rect.height > 0)) {
            cancelMouse();
            return;
        }
        if (event.buttons && mousePointer === null) {
            try {
                canvas.setPointerCapture(event.pointerId);
                if (!canvas.hasPointerCapture(event.pointerId)) throw new Error("Mouse capture failed.");
                mousePointer = event.pointerId;
            } catch {
                cancelMouse();
                return;
            }
            if (handle.preserveFocus) event.preventDefault();
            else if (![...handle.proxies.values()].includes(document.activeElement)) canvas.focus({ preventScroll: true });
        }
        const position = { type: 1, x: (event.clientX - rect.left) * canvas.clientWidth / rect.width, y: (event.clientY - rect.top) * canvas.clientHeight / rect.height };
        let changed = false;
        for (const [mask, button] of masks) {
            const down = !!(event.buttons & mask);
            if (down === handle.buttons.has(button)) continue;
            changed = true;
            if (down) handle.buttons.add(button); else handle.buttons.delete(button);
            // Position and button are one acquisition and must remain adjacent.
            const capture = ++handle.captureSequence;
            record({ ...position, capture }, { type: 2, x: button, down, capture });
        }
        if (!changed) record({ ...position, capture: ++handle.captureSequence });
        if (event.buttons === 0 && mousePointer !== null) {
            const pointer = mousePointer;
            mousePointer = null;
            if (canvas.hasPointerCapture(pointer)) canvas.releasePointerCapture(pointer);
        }
    };
    handle.captureSequence = 0;
    for (const name of ["pointerdown", "pointermove", "pointerup", "pointercancel"]) listen(name, mouse);
    listen("lostpointercapture", event => { if (event.pointerId === mousePointer) cancelMouse(); });
    listenWindow("pointerup", event => {
        if (event.pointerType === "mouse" && blocked) {
            physicalButtons = event.buttons;
            if (!physicalButtons) blocked = false;
        }
    });
    const visibility = () => { if (document.hidden || !canvas.isConnected) cancelMouse(); };
    document.addEventListener("visibilitychange", visibility);
    handle.visibility = visibility;
    handle.connectionObserver = new MutationObserver(visibility);
    handle.connectionObserver.observe(document, { childList: true, subtree: true });
    listen("wheel", event => {
        const divisor = event.deltaMode === 0 ? 120 : event.deltaMode === 1 ? 3 : 1;
        record({ type: 3, x: event.deltaX / divisor, y: -event.deltaY / divisor });
    });
    handle.setLogicalFocus = focused => {
        if (handle.logicalFocus !== focused) {
            handle.logicalFocus = focused;
            if (!focused) cancelMouse();
            record({ type: 4, down: focused });
        }
        handle.capture();
    };
    listen("focus", () => handle.setLogicalFocus(true));
    listen("blur", event => {
        if (!handle.ownsFocus(event.relatedTarget)) handle.setLogicalFocus(false);
    });
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
    handle.runTextFocusBridge = event => {
        if (!handle.textFocusBridge) return true;
        if (handle.textBridgeBusy) { event.preventDefault(); event.stopImmediatePropagation(); return false; }
        handle.textBridgeBusy = true;
        try {
            if (!handle.textFocusBridge()) {
                handle.input.splice(handle.textBoundaryStart ?? handle.input.length);
                event.preventDefault(); event.stopImmediatePropagation();
                return false;
            } else if (event.type.startsWith("pointer") && document.activeElement !== canvas && handle.ownsFocus(document.activeElement)) {
                event.preventDefault();
            }
            return true;
        } finally { handle.textBridgeBusy = false; }
    };
    for (const name of ["pointerdown", "pointerup", "pointermove", "keydown", "keyup"]) {
        const start = () => { handle.textBoundaryStart = handle.input.length; };
        canvas.addEventListener(name, start, true);
        handle.textBoundaryListeners ??= [];
        handle.textBoundaryListeners.push([name,start]);
        listen(name, event => {
            if (name === "pointermove" && event.buttons === 0) return;
            handle.runTextFocusBridge(event);
        });
    }
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
    return [width, height, pixelWidth, pixelHeight, scale, visible ? 1 : 0, document.hasFocus() && handle.ownsFocus(document.activeElement) ? 1 : 0, connected ? 1 : 0];
}

export function drain(handle) {
    const result = handle.events;
    handle.events = [];
    return result;
}

export function show(handle, visible) {
    if (!visible) handle.cancelMouse();
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
    handle.cancelMouse();
    handle.detached = true;
    handle.connectionObserver.disconnect();
    for (const [name,fn] of handle.textBoundaryListeners ?? []) handle.canvas.removeEventListener(name,fn,true);
    for (const token of [...handle.proxyListeners.keys()]) unregisterTextProxy(handle, token);
    document.removeEventListener("visibilitychange", handle.visibility);
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
    const events = handle.input.splice(0);
    if (handle.inputFault) events.push({ type: 6 });
    return JSON.stringify(events);
}

export function setInputEnabled(handle, enabled) {
    if (!enabled) handle.cancelMouse();
    handle.inputEnabled = enabled;
    handle.inputFault = false;
    handle.input.length = 0;
    if (enabled) handle.input.push({ type: 4, down: document.hasFocus() && document.activeElement === handle.canvas });
}

export function registerTextProxy(handle, proxy, token) {
    if (handle.detached || handle.proxies.has(token)) throw new Error("Expired or duplicate text proxy registration.");
    handle.proxies.set(token, proxy);
    proxy.lumyteBlockedKeys = new Set([...handle.keyboardHeld, ...handle.textConsumedKeys]);
    proxy.lumyteHeldKeys = handle.keyboardHeld;
    const focus = () => handle.setLogicalFocus(true);
    const blur = event => { if (!handle.ownsFocus(event.relatedTarget)) handle.setLogicalFocus(false); };
    proxy.addEventListener("focus", focus);
    proxy.addEventListener("blur", blur);
    handle.proxyListeners.set(token, {proxy, focus, blur});
    proxy.lumyteFocusBridge = event => handle.runTextFocusBridge(event);
}

export function unregisterTextProxy(handle, token) {
    const listeners = handle.proxyListeners.get(token);
    if (!listeners) return;
    listeners.proxy.removeEventListener("focus", listeners.focus);
    listeners.proxy.removeEventListener("blur", listeners.blur);
    for (const code of listeners.proxy.lumyteConsumedKeys ?? []) handle.textConsumedKeys.add(code);
    delete listeners.proxy.lumyteConsumedKeys;
    delete listeners.proxy.lumyteBlockedKeys;
    delete listeners.proxy.lumyteHeldKeys;
    delete listeners.proxy.lumyteFocusBridge;
    handle.proxyListeners.delete(token);
    handle.proxies.delete(token);
    if (!handle.ownsFocus(document.activeElement)) handle.setLogicalFocus(false);
}

export function setTextFocusBridge(handle, bridge) { handle.textFocusBridge = bridge; }

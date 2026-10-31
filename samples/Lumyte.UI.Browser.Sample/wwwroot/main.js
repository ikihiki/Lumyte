import { dotnet } from "./_framework/dotnet.js";
const status = document.querySelector("#status");
try {
    globalThis.lumytePopups = new URLSearchParams(location.search).has("popups");
    globalThis.lumyteDiagnostics = new URLSearchParams(location.search).has("diagnostics");
    globalThis.lumyteAutomation = new URLSearchParams(location.search).has("automation");
    globalThis.lumyteInputOnly = new URLSearchParams(location.search).has("input-only");
    globalThis.lumyteRenderGraph = new URLSearchParams(location.search).has("render-graph");
    globalThis.lumyteResources = new URLSearchParams(location.search).has("resources");
    globalThis.lumyteUiCanvas = document.querySelector("canvas");
    const runtime = await dotnet.create();
    const running = runtime.runMain();
    running.catch(error => { status.textContent = String(error); console.error(error); });
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    const ui = exports.Lumyte.UI.Browser.Sample.Program;
    globalThis.lumyteUi = ui;
    let observingOutside = false;
    const outside = event => {
        if (!globalThis.lumyteUiCanvas.contains(event.target)) ui.OutsidePopupPress();
    };
    const contextMenu = event => {
        event.preventDefault(); // Only this canvas; opening is queued on the C# UI owner.
        ui.OpenPopup(true);
    };
    if (globalThis.lumytePopups) globalThis.lumyteUiCanvas.addEventListener("contextmenu", contextMenu);
    let stopped = false;
    let inFlight = false;
    let finished = false;
    async function stop() {
        if (!finished) {
            finished = true;
            document.removeEventListener("pointerdown", outside, true);
            globalThis.lumyteUiCanvas.removeEventListener("contextmenu", contextMenu);
            await ui.StopAsync();
        }
    }
    async function frame() {
        if (finished) return;
        inFlight = true;
        try {
            if (globalThis.lumytePopups && !globalThis.lumyteUiCanvas.isConnected) { stopped = true; return; }
            await ui.FrameAsync();
            if (globalThis.lumytePopups) {
                const open = Number(ui.GetPopupState().split(",")[3]) > 0;
                if (open && !observingOutside) document.addEventListener("pointerdown", outside, true);
                if (!open && observingOutside) document.removeEventListener("pointerdown", outside, true);
                observingOutside = open;
            }
        } catch (error) {
            stopped = true;
            status.textContent = String(error);
            console.error(error);
        } finally {
            inFlight = false;
            if (stopped) await stop();
            else requestAnimationFrame(frame);
        }
    }
    document.querySelector("button").onclick = () => {
        stopped = true;
        if (!inFlight) stop();
    };
    status.textContent = globalThis.lumytePopups ? "青い領域を右クリックするとメニューが開きます。緑は保存、橙はサブメニューです。" : "Click the blue or orange UI. Tab / Shift-Tab changes focus; Space activates.";
    if (globalThis.lumyteAutomation) await ui.RunAutomationAsync();
    requestAnimationFrame(frame);
} catch (error) { status.textContent = String(error); console.error(error); }

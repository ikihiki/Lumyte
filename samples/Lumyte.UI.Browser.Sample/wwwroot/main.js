import { dotnet } from "./_framework/dotnet.js";
const status = document.querySelector("#status");
try {
    globalThis.lumyteInputOnly = new URLSearchParams(location.search).has("input-only");
    globalThis.lumyteUiCanvas = document.querySelector("canvas");
    const runtime = await dotnet.create();
    const running = runtime.runMain();
    running.catch(error => { status.textContent = String(error); console.error(error); });
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    const ui = exports.Lumyte.UI.Browser.Sample.Program;
    globalThis.lumyteUi = ui;
    let stopped = false;
    let inFlight = false;
    let finished = false;
    function stop() {
        if (!finished) {
            ui.Stop();
            finished = true;
        }
    }
    async function frame() {
        if (finished) return;
        inFlight = true;
        try {
            await ui.FrameAsync();
        } catch (error) {
            stopped = true;
            status.textContent = String(error);
            console.error(error);
        } finally {
            inFlight = false;
            if (stopped) stop();
            else requestAnimationFrame(frame);
        }
    }
    document.querySelector("button").onclick = () => {
        stopped = true;
        if (!inFlight) stop();
    };
    status.textContent = "Click the blue or orange UI. Tab / Shift-Tab changes focus; Space activates.";
    requestAnimationFrame(frame);
} catch (error) { status.textContent = String(error); console.error(error); }

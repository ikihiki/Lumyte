import { dotnet } from "./_framework/dotnet.js";
globalThis.lumyteApplicationFinished = false;
try {
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    globalThis.lumyteApplicationProbe = exports.Lumyte.UI.Transitions.Browser.Sample.BrowserProbe;
    await runtime.runMain(runtime.getConfig().mainAssemblyName, [...new URLSearchParams(location.search).keys()].filter(key => key === "sequential" || key === "same-content").map(key => `--${key}`));
    globalThis.lumyteApplicationFinished = true;
} catch (error) {
    globalThis.lumyteApplicationError = String(error);
    console.error(error);
}

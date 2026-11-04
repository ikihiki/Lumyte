import { dotnet } from "./_framework/dotnet.js";
globalThis.lumyteApplicationFinished = false;
try {
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    globalThis.lumyteApplicationProbe = exports.Lumyte.UI.Application.Browser.Sample.BrowserProbe;
    await runtime.runMain();
    globalThis.lumyteApplicationFinished = true;
} catch (error) {
    globalThis.lumyteApplicationError = String(error);
    console.error(error);
}

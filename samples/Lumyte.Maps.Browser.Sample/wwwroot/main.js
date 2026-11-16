import { dotnet } from "./_framework/dotnet.js";
globalThis.lumyteMapsFinished = false;
try {
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    globalThis.lumyteMapsProbe = exports.Lumyte.Maps.Browser.Sample.BrowserProbe;
    await runtime.runMain();
    globalThis.lumyteMapsFinished = true;
} catch (error) {
    globalThis.lumyteMapsError = String(error);
    console.error(error);
}

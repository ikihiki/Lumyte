import { dotnet } from "./_framework/dotnet.js";
globalThis.lumyteApplicationFinished = false;
try {
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    globalThis.lumyteApplicationProbe = exports.Lumyte.UI.Navigation.Browser.Sample.BrowserProbe;
    if (globalThis.__lumyteCaptureDiagnosticsRequested === true) globalThis.lumyteApplicationProbe.EnableCaptureDiagnostics(payload => globalThis.__lumyteCaptureDiagnosticEvent(payload));
    await runtime.runMain();
    globalThis.lumyteApplicationFinished = true;
} catch (error) {
    globalThis.lumyteApplicationError = String(error);
    console.error(error);
}

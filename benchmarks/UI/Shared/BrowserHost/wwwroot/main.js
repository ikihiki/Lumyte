import { dotnet } from "./_framework/dotnet.js";
try {
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    globalThis.lumyteBenchmark = exports.Lumyte.UI.BrowserBenchmarkHost.BrowserWorkload;
    runtime.runMain().catch(error => { globalThis.lumyteBenchmarkError = String(error); });
} catch (error) { globalThis.lumyteBenchmarkError = String(error); }

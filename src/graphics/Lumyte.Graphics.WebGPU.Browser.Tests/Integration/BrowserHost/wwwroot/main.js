import { dotnet } from './_framework/dotnet.js';

try {
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    await runtime.runMain();
    globalThis.resolveLumyteHost(exports.BrowserCases);
    document.querySelector('#status').textContent = 'The .NET browser host is ready.';
} catch (error) {
    globalThis.rejectLumyteHost(error);
    document.querySelector('#status').textContent = error.stack ?? String(error);
}

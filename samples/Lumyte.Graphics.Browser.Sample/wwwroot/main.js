import { dotnet } from "./_framework/dotnet.js";

const output = document.getElementById("caps");
try {
    const runtime = await dotnet.create();
    const exitCode = await runtime.runMain();
    if (exitCode !== 0) {
        throw new Error(`.NET exited with code ${exitCode}`);
    }
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    output.textContent = exports.Lumyte.Graphics.Browser.Sample.Program.GetReport();
    output.dataset.result = "passed";
} catch (error) {
    output.textContent = String(error);
    output.dataset.result = "failed";
    console.error(error);
}
